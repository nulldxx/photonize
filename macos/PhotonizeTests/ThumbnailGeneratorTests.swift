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

    /// Video recognition is case-insensitive and limited to the Windows three;
    /// images and unsupported containers are not videos.
    func testIsVideoFileRecognizesExtensions() {
        let generator = ThumbnailGenerator()
        for name in ["clip.mp4", "clip.MOV", "clip.m4v", "CLIP.Mp4"] {
            XCTAssertTrue(generator.isVideoFile(URL(fileURLWithPath: "/tmp/\(name)")),
                          "\(name) should be recognised as video")
        }
        for name in ["photo.jpg", "photo.png", "clip.mkv", "clip.avi", "clip.webm"] {
            XCTAssertFalse(generator.isVideoFile(URL(fileURLWithPath: "/tmp/\(name)")),
                           "\(name) should not be recognised as video")
        }

        // isMediaFile is the union of images and videos.
        XCTAssertTrue(generator.isMediaFile(URL(fileURLWithPath: "/tmp/a.jpg")))
        XCTAssertTrue(generator.isMediaFile(URL(fileURLWithPath: "/tmp/a.mov")))
        XCTAssertFalse(generator.isMediaFile(URL(fileURLWithPath: "/tmp/a.mkv")))
    }

    /// The thumbnail frame time is 0.1 s for a very short clip (where 1 s would be
    /// at or past the end) and 1 s for a normal one — matching Windows.
    func testVideoThumbnailTimestamp() {
        XCTAssertEqual(ThumbnailGenerator.videoThumbnailSeconds(forDuration: 1), 0.1, accuracy: 0.0001)
        XCTAssertEqual(ThumbnailGenerator.videoThumbnailSeconds(forDuration: 0.5), 0.1, accuracy: 0.0001)
        XCTAssertEqual(ThumbnailGenerator.videoThumbnailSeconds(forDuration: 30), 1, accuracy: 0.0001)
        XCTAssertEqual(ThumbnailGenerator.videoThumbnailSeconds(forDuration: 2), 1, accuracy: 0.0001)
    }

    /// The cache key must distinguish an image and a video frame for the same
    /// path/size, so the two never collide.
    func testCacheKeyDistinguishesVideoFromImage() {
        let url = URL(fileURLWithPath: "/tmp/does-not-exist-\(UUID().uuidString).mp4")
        XCTAssertNotEqual(
            ThumbnailGenerator.cacheKey(for: url, size: 200, isVideo: true),
            ThumbnailGenerator.cacheKey(for: url, size: 200, isVideo: false)
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
