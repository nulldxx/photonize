import Foundation
import CoreGraphics

// The only file in the project that touches libwebp's C API — everything else
// speaks in `Data`. Encoding is macOS 14+ in ImageIO, but the deployment target
// is 13.0, so we vendor libwebp (via SPM) to keep Ventura users covered.
//
// The `canImport` guard lets the same source both build in Xcode (where the SPM
// module resolves) and type-check on a Command-Line-Tools-only machine (where a
// local shim supplies `WebPEncodeRGBA`/`WebPFree`); in a real build the module
// is always present.
#if canImport(libwebp)
import libwebp
#endif

enum WebPEncoderError: Error {
    case invalidImage
    case encodeFailed
}

enum WebPEncoder {
    /// Encode a `CGImage` to lossy WebP at `quality` (0–100). Windows uses
    /// `WebpEncoder { Quality = 90, FileFormat = Lossy }`; passing 90 here keeps
    /// the two clients' output interchangeable.
    static func encode(_ cgImage: CGImage, quality: Float = 90) throws -> Data {
        let width = cgImage.width
        let height = cgImage.height
        guard width > 0, height > 0 else { throw WebPEncoderError.invalidImage }

        // Draw into a known RGBA8, top-down buffer so libwebp gets exactly the
        // layout `WebPEncodeRGBA` expects rather than whatever the source CGImage
        // happens to use.
        let bytesPerRow = width * 4
        var rgba = [UInt8](repeating: 0, count: bytesPerRow * height)
        let colorSpace = CGColorSpaceCreateDeviceRGB()
        let bitmapInfo = CGImageAlphaInfo.premultipliedLast.rawValue
            | CGBitmapInfo.byteOrder32Big.rawValue

        let encoded: Data? = rgba.withUnsafeMutableBytes { raw in
            guard let base = raw.baseAddress,
                  let ctx = CGContext(
                    data: base,
                    width: width,
                    height: height,
                    bitsPerComponent: 8,
                    bytesPerRow: bytesPerRow,
                    space: colorSpace,
                    bitmapInfo: bitmapInfo
                  )
            else { return nil }

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
        return encoded
    }
}
