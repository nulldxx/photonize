import Foundation
import CoreGraphics

// The only file in the project that touches libwebp's C API — everything else
// speaks in `Data`. Encoding is macOS 14+ in ImageIO, but the deployment target
// is 13.0, so we vendor libwebp (via SPM) to keep Ventura users covered.
//
// The `canImport` guard lets the same source both build in Xcode (where the SPM
// module resolves) and type-check on a Command-Line-Tools-only machine (where a
// local shim supplies `WebPEncodeRGBA`/`WebPFree` and the `WebPMux…` calls); in a
// real build the module is always present.
#if canImport(libwebp)
import libwebp
#endif

enum WebPEncoderError: Error {
    case invalidImage
    case encodeFailed
    case muxFailed
}

enum WebPEncoder {
    /// Encode a `CGImage` to lossy WebP at `quality` (0–100). Windows uses
    /// `WebpEncoder { Quality = 90, FileFormat = Lossy }`; passing 90 here keeps
    /// the two clients' output interchangeable.
    ///
    /// `exif` (raw TIFF bytes) and `xmp` are written as WebP metadata chunks. The
    /// image's own RGB colour space is kept and embedded as an ICC profile, so a
    /// Display P3 photo stays P3 rather than being flattened to device RGB.
    static func encode(
        _ cgImage: CGImage,
        quality: Float = 90,
        exif: Data? = nil,
        xmp: Data? = nil
    ) throws -> Data {
        let width = cgImage.width
        let height = cgImage.height
        guard width > 0, height > 0 else { throw WebPEncoderError.invalidImage }

        let sRGB = CGColorSpace(name: CGColorSpace.sRGB)!
        let preferred = cgImage.colorSpace.flatMap { $0.model == .rgb ? $0 : nil } ?? sRGB

        // Draw into a known RGBA8, top-down buffer so libwebp gets exactly the
        // layout `WebPEncodeRGBA` expects rather than whatever the source CGImage
        // happens to use. Some RGB spaces (extended-range, HDR) can't back an 8-bit
        // context; those fall back to sRGB.
        let bytesPerRow = width * 4
        var rgba = [UInt8](repeating: 0, count: bytesPerRow * height)
        let bitmapInfo = CGImageAlphaInfo.premultipliedLast.rawValue
            | CGBitmapInfo.byteOrder32Big.rawValue

        var usedSpace = preferred
        let encoded: Data? = rgba.withUnsafeMutableBytes { raw in
            guard let base = raw.baseAddress else { return nil }

            func context(_ space: CGColorSpace) -> CGContext? {
                CGContext(
                    data: base, width: width, height: height, bitsPerComponent: 8,
                    bytesPerRow: bytesPerRow, space: space, bitmapInfo: bitmapInfo
                )
            }
            var ctx = context(preferred)
            if ctx == nil {
                usedSpace = sRGB
                ctx = context(sRGB)
            }
            guard let ctx else { return nil }

            ctx.draw(cgImage, in: CGRect(x: 0, y: 0, width: width, height: height))

            var output: UnsafeMutablePointer<UInt8>? = nil
            let count = WebPEncodeRGBA(
                base.assumingMemoryBound(to: UInt8.self),
                Int32(width), Int32(height), Int32(bytesPerRow),
                quality, &output
            )
            guard count > 0, let output else { return nil }
            defer { WebPFree(output) }
            return Data(bytes: output, count: count)
        }

        guard let encoded else { throw WebPEncoderError.encodeFailed }

        let chunks: [(fourCC: String, data: Data?)] = [
            ("ICCP", usedSpace.copyICCData() as Data?),
            ("EXIF", exif),
            ("XMP ", xmp),
        ]
        return try addChunks(chunks.compactMap { c in c.data.map { (c.fourCC, $0) } }, to: encoded)
    }

    /// Re-wraps a plain VP8 bitstream in an extended (VP8X) container carrying the
    /// given metadata chunks. libwebp's mux sets the VP8X flags for each chunk.
    private static func addChunks(_ chunks: [(String, Data)], to bitstream: Data) throws -> Data {
        let chunks = chunks.filter { !$0.1.isEmpty }
        guard !chunks.isEmpty else { return bitstream }

        guard let mux = WebPMuxNew() else { throw WebPEncoderError.muxFailed }
        defer { WebPMuxDelete(mux) }

        // copy_data = 1 throughout, so the mux owns its bytes and nothing has to
        // outlive these closures.
        try bitstream.withUnsafeBytes { raw in
            var image = WebPData(bytes: raw.bindMemory(to: UInt8.self).baseAddress, size: raw.count)
            guard WebPMuxSetImage(mux, &image, 1) == WEBP_MUX_OK else { throw WebPEncoderError.muxFailed }
        }
        for (fourCC, data) in chunks {
            try data.withUnsafeBytes { raw in
                var chunk = WebPData(bytes: raw.bindMemory(to: UInt8.self).baseAddress, size: raw.count)
                guard WebPMuxSetChunk(mux, fourCC, &chunk, 1) == WEBP_MUX_OK else {
                    throw WebPEncoderError.muxFailed
                }
            }
        }

        var assembled = WebPData(bytes: nil, size: 0)
        guard WebPMuxAssemble(mux, &assembled) == WEBP_MUX_OK, let bytes = assembled.bytes else {
            throw WebPEncoderError.muxFailed
        }
        defer { WebPDataClear(&assembled) }
        return Data(bytes: bytes, count: assembled.size)
    }
}
