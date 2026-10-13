import Foundation
import CoreGraphics
import ImageIO
import UniformTypeIdentifiers

/// Carries a source photo's metadata (EXIF, GPS, TIFF, IPTC, XMP) onto an export.
/// ImageIO writes it straight into JPEG/PNG; WebP goes through libwebp, which wants
/// raw EXIF and XMP bytes, so this also produces those.
enum ImageMetadata {
    /// The source's metadata, adjusted for an export whose pixels have already had
    /// the EXIF orientation applied: Orientation becomes 1 (upright) so viewers don't
    /// rotate the image a second time. Returns nil when the source carries none.
    static func uprightMetadata(from source: CGImageSource) -> CGImageMetadata? {
        guard let original = CGImageSourceCopyMetadataAtIndex(source, 0, nil),
              let metadata = CGImageMetadataCreateMutableCopy(original) else { return nil }

        if CGImageMetadataCopyTagMatchingImageProperty(
            metadata, kCGImagePropertyTIFFDictionary, kCGImagePropertyTIFFOrientation
        ) != nil {
            CGImageMetadataSetValueMatchingImageProperty(
                metadata, kCGImagePropertyTIFFDictionary, kCGImagePropertyTIFFOrientation, 1 as CFNumber
            )
        }
        return metadata
    }

    /// The same metadata with the EXIF pixel dimensions set to the export's size,
    /// which differs from the source's when orientation swapped width and height.
    static func withPixelSize(_ metadata: CGImageMetadata, width: Int, height: Int) -> CGImageMetadata {
        guard let copy = CGImageMetadataCreateMutableCopy(metadata) else { return metadata }
        for (property, value) in [
            (kCGImagePropertyExifPixelXDimension, width),
            (kCGImagePropertyExifPixelYDimension, height),
        ] where CGImageMetadataCopyTagMatchingImageProperty(copy, kCGImagePropertyExifDictionary, property) != nil {
            CGImageMetadataSetValueMatchingImageProperty(copy, kCGImagePropertyExifDictionary, property, value as CFNumber)
        }
        return copy
    }

    /// Serialised XMP packet for a WebP `XMP ` chunk.
    static func xmpData(from metadata: CGImageMetadata) -> Data? {
        CGImageMetadataCreateXMPData(metadata, nil) as Data?
    }

    /// Raw EXIF (a TIFF structure, starting `II*`/`MM*`) for a WebP `EXIF` chunk.
    ///
    /// ImageIO only exposes EXIF as a dictionary, so this has it write the metadata
    /// into a tiny JPEG and lifts the APP1 segment back out. ImageIO stamps that
    /// stand-in's 8×8 into the EXIF pixel dimensions whatever the metadata says, so
    /// they are patched to `width`×`height` afterwards.
    static func exifBlob(from metadata: CGImageMetadata, width: Int, height: Int) -> Data? {
        guard let space = CGColorSpace(name: CGColorSpace.sRGB),
              let ctx = CGContext(
                data: nil, width: 8, height: 8, bitsPerComponent: 8, bytesPerRow: 0,
                space: space, bitmapInfo: CGImageAlphaInfo.noneSkipLast.rawValue
              ),
              let image = ctx.makeImage()
        else { return nil }

        let data = NSMutableData()
        guard let dest = CGImageDestinationCreateWithData(
            data as CFMutableData, UTType.jpeg.identifier as CFString, 1, nil
        ) else { return nil }
        CGImageDestinationAddImageAndMetadata(dest, image, metadata, nil)
        guard CGImageDestinationFinalize(dest) else { return nil }

        return exifPayload(inJPEG: data as Data).map {
            settingPixelDimensions(inTIFF: $0, width: width, height: height)
        }
    }

    /// Rewrites `PixelXDimension`/`PixelYDimension` in a raw EXIF (TIFF) blob's Exif
    /// IFD. Both are single values stored inline, so this is an in-place patch; a
    /// SHORT too small for the new size is widened to a LONG, which still fits. A
    /// blob that doesn't parse is returned unchanged.
    static func settingPixelDimensions(inTIFF tiff: Data, width: Int, height: Int) -> Data {
        var b = [UInt8](tiff)
        guard b.count >= 8 else { return tiff }
        let little: Bool
        switch (b[0], b[1]) {
        case (0x49, 0x49): little = true
        case (0x4D, 0x4D): little = false
        default: return tiff
        }

        func read(_ at: Int, _ size: Int) -> Int? {
            guard at >= 0, at + size <= b.count else { return nil }
            var v = 0
            for k in 0..<size { v |= Int(b[at + (little ? k : size - 1 - k)]) << (8 * k) }
            return v
        }
        func write(_ at: Int, _ size: Int, _ value: Int) {
            for k in 0..<size { b[at + (little ? k : size - 1 - k)] = UInt8((value >> (8 * k)) & 0xFF) }
        }
        /// Byte offset of each 12-byte entry in the IFD at `offset`.
        func entries(_ offset: Int) -> [Int] {
            guard let count = read(offset, 2) else { return [] }
            return (0..<count).map { offset + 2 + $0 * 12 }.filter { $0 + 12 <= b.count }
        }

        guard let ifd0 = read(4, 4),
              let pointer = entries(ifd0).first(where: { read($0, 2) == 0x8769 }),
              let exifIFD = read(pointer + 8, 4)
        else { return tiff }

        let shortType = 3, longType = 4
        for entry in entries(exifIFD) {
            let value: Int
            switch read(entry, 2) {
            case 0xA002?: value = width
            case 0xA003?: value = height
            default: continue
            }
            guard read(entry + 4, 4) == 1, let type = read(entry + 2, 2) else { continue }
            if type == shortType, value <= 0xFFFF {
                write(entry + 8, 4, 0)
                write(entry + 8, 2, value)
            } else if type == shortType || type == longType {
                write(entry + 2, 2, longType)
                write(entry + 8, 4, value)
            }
        }
        return Data(b)
    }

    /// The TIFF payload of a JPEG's `Exif\0\0` APP1 segment, or nil if it has none.
    static func exifPayload(inJPEG jpeg: Data) -> Data? {
        let bytes = [UInt8](jpeg)
        guard bytes.count > 4, bytes[0] == 0xFF, bytes[1] == 0xD8 else { return nil }

        let exifHeader: [UInt8] = [0x45, 0x78, 0x69, 0x66, 0x00, 0x00]   // "Exif\0\0"
        var i = 2
        while i + 4 <= bytes.count, bytes[i] == 0xFF {
            let marker = bytes[i + 1]
            // Start of scan or end of image: the metadata segments are behind us.
            if marker == 0xDA || marker == 0xD9 { break }

            let length = Int(bytes[i + 2]) << 8 | Int(bytes[i + 3])
            let start = i + 4
            let end = i + 2 + length
            guard length >= 2, end <= bytes.count else { return nil }

            if marker == 0xE1, end - start > exifHeader.count,
               Array(bytes[start..<start + exifHeader.count]) == exifHeader {
                return Data(bytes[(start + exifHeader.count)..<end])
            }
            i = end
        }
        return nil
    }
}
