import XCTest
import AppKit
import ImageIO
import UniformTypeIdentifiers
@testable import Photonize

// NOTE: No test target yet (slice 12 adds it). Kept alongside the code for now.
final class ThumbnailGeneratorTests: XCTestCase {

    /// The cache key must change when the file's mtime changes, so an edited file
    /// re-renders rather than serving a stale bitmap.
    func testCacheKeyChangesWithMtime() throws {
        let url = FileManager.default.temporaryDirectory
            .appendingPathComponent("photonize-mtime-\(UUID().uuidString).txt")
        try "one".write(to: url, atomically: true, encoding: .utf8)
        defer { try? FileManager.default.removeItem(at: url) }

        let keyBefore = ThumbnailGenerator.cacheKey(for: url, size: 200)

        // Bump the modification date.
        try FileManager.default.setAttributes(
            [.modificationDate: Date().addingTimeInterval(120)],
            ofItemAtPath: url.path
        )

        let keyAfter = ThumbnailGenerator.cacheKey(for: url, size: 200)
        XCTAssertNotEqual(keyBefore, keyAfter)

        // And the size component still distinguishes slider buckets.
        XCTAssertNotEqual(
            ThumbnailGenerator.cacheKey(for: url, size: 200),
            ThumbnailGenerator.cacheKey(for: url, size: 400)
        )
    }

    /// A landscape image tagged with EXIF orientation 6 (rotate 90° CW) must come
    /// back portrait-shaped once the orientation transform is applied.
    func testOrientationSixProducesPortraitThumbnail() async throws {
        let url = try makeFixture(pixelWidth: 120, pixelHeight: 60, orientation: 6)
        defer { try? FileManager.default.removeItem(at: url) }

        let thumbnail = await ThumbnailGenerator().generateThumbnail(for: url, maxSize: 200)
        let image = try XCTUnwrap(thumbnail)
        XCTAssertGreaterThan(image.size.height, image.size.width,
                             "orientation 6 should rotate the landscape source to portrait")
    }

    /// Writes a solid-colour image of the given pixel dimensions with an EXIF
    /// orientation tag to a temp JPEG.
    private func makeFixture(pixelWidth: Int, pixelHeight: Int, orientation: Int) throws -> URL {
        let colorSpace = CGColorSpaceCreateDeviceRGB()
        let context = CGContext(
            data: nil,
            width: pixelWidth,
            height: pixelHeight,
            bitsPerComponent: 8,
            bytesPerRow: 0,
            space: colorSpace,
            bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue
        )!
        context.setFillColor(CGColor(red: 0.2, green: 0.5, blue: 0.9, alpha: 1))
        context.fill(CGRect(x: 0, y: 0, width: pixelWidth, height: pixelHeight))
        let cgImage = context.makeImage()!

        let url = FileManager.default.temporaryDirectory
            .appendingPathComponent("photonize-orient-\(UUID().uuidString).jpg")
        let dest = CGImageDestinationCreateWithURL(url as CFURL, UTType.jpeg.identifier as CFString, 1, nil)!
        let props: [CFString: Any] = [kCGImagePropertyOrientation: orientation]
        CGImageDestinationAddImage(dest, cgImage, props as CFDictionary)
        guard CGImageDestinationFinalize(dest) else {
            throw XCTSkip("could not write orientation fixture")
        }
        return url
    }
}
