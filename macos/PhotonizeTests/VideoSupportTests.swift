import XCTest
import AppKit
import ImageIO
import UniformTypeIdentifiers
@testable import Photonize

// NOTE: There is no test target yet — slice 12 creates it. These tests are kept
// here alongside the code they cover so they are ready to wire in.
final class VideoSupportTests: XCTestCase {

    /// A mixed folder enumerates images and videos together, in one
    /// filename-sorted sequence, each flagged with the right `isVideo`. This is
    /// the filter/sort `loadPhotos` applies, exercised directly on the generator.
    func testMixedFolderEnumeration() throws {
        let dir = try makeTempDir()
        defer { try? FileManager.default.removeItem(at: dir) }

        // Deliberately out of order on disk; names chosen so the sorted order
        // interleaves images and videos.
        for name in ["d.mov", "a.jpg", "c.png", "b.mp4", "notes.txt", "e.mkv"] {
            try Data("x".utf8).write(to: dir.appendingPathComponent(name))
        }

        let generator = ThumbnailGenerator()
        let media = (try FileManager.default.contentsOfDirectory(
            at: dir, includingPropertiesForKeys: nil, options: [.skipsHiddenFiles]
        ))
        .filter { generator.isMediaFile($0) }
        .sorted { $0.lastPathComponent < $1.lastPathComponent }

        // notes.txt and e.mkv are excluded; the rest come back name-sorted.
        XCTAssertEqual(media.map { $0.lastPathComponent }, ["a.jpg", "b.mp4", "c.png", "d.mov"])

        // Classification lands on the video files only.
        let videoFlags = media.map { generator.isVideoFile($0) }
        XCTAssertEqual(videoFlags, [false, true, false, true])
    }

    /// Exporting a mixed selection produces outputs only for the images and reports
    /// the video count as skipped, not failed.
    func testMixedExportSkipsVideos() async throws {
        let dir = try makeTempDir()
        defer { try? FileManager.default.removeItem(at: dir) }

        // Three real image sources plus two videos (contents irrelevant — they are
        // partitioned out before the exporter ever reads them).
        var sources: [PhotoItem] = []
        for i in 0..<3 {
            let url = dir.appendingPathComponent("img\(i).png")
            try encodePNG(makeTestImage(width: 32, height: 32)).write(to: url)
            sources.append(PhotoItem(filePath: url, fileName: url.lastPathComponent, displayOrder: i, isFolder: false, thumbnail: nil))
        }
        for i in 0..<2 {
            let url = dir.appendingPathComponent("clip\(i).mp4")
            try Data("not a real video".utf8).write(to: url)
            sources.append(PhotoItem(filePath: url, fileName: url.lastPathComponent, displayOrder: 3 + i, isFolder: false, thumbnail: nil, isVideo: true))
        }

        let (images, unsupported) = PhotoGridViewModel.partitionExportSources(sources)
        XCTAssertEqual(unsupported, 2, "both videos should be skipped")
        XCTAssertEqual(images.count, 3)
        XCTAssertTrue(images.allSatisfy { !$0.isVideo })

        let exporter = ImageExporter()
        let result = try await exporter.export(
            images, in: dir, as: .png,
            overwrite: { _ in .overwriteAll },
            progress: { _, _, _ in }
        )

        XCTAssertEqual(result.exported, 3, "only the images are exported")
        XCTAssertTrue(result.failed.isEmpty, "videos are skipped up front, never failed")

        // Exactly three outputs land in the PNG folder — no video-derived files.
        let outputs = try FileManager.default.contentsOfDirectory(atPath: dir.appendingPathComponent("PNG").path)
        XCTAssertEqual(outputs.filter { $0.hasSuffix(".png") }.count, 3)
    }

    // MARK: - Helpers

    private func makeTempDir() throws -> URL {
        let url = FileManager.default.temporaryDirectory.appendingPathComponent("VideoSupportTests-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: url, withIntermediateDirectories: true)
        return url
    }

    private func makeTestImage(width: Int, height: Int) -> CGImage {
        let colorSpace = CGColorSpaceCreateDeviceRGB()
        let context = CGContext(
            data: nil, width: width, height: height, bitsPerComponent: 8, bytesPerRow: 0,
            space: colorSpace, bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue
        )!
        context.setFillColor(CGColor(red: 0.3, green: 0.6, blue: 0.9, alpha: 1))
        context.fill(CGRect(x: 0, y: 0, width: width, height: height))
        return context.makeImage()!
    }

    private func encodePNG(_ image: CGImage) throws -> Data {
        let data = NSMutableData()
        let dest = CGImageDestinationCreateWithData(data as CFMutableData, UTType.png.identifier as CFString, 1, nil)!
        CGImageDestinationAddImage(dest, image, nil)
        guard CGImageDestinationFinalize(dest) else { throw ExportError.encodeFailed }
        return data as Data
    }
}
