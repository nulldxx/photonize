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
    /// into a tiny JPEG and lifts the APP1 segment back out. The EXIF pixel dimensions
    /// are dropped first so the stand-in's 8×8 can't leak into them; the WebP header
    /// carries the real size.
    static func exifBlob(from metadata: CGImageMetadata) -> Data? {
        guard let metadata = CGImageMetadataCreateMutableCopy(metadata) else { return nil }
        CGImageMetadataRemoveTagWithPath(metadata, nil, "exif:PixelXDimension" as CFString)
        CGImageMetadataRemoveTagWithPath(metadata, nil, "exif:PixelYDimension" as CFString)

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

        return exifPayload(inJPEG: data as Data)
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
