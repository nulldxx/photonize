import XCTest
import AppKit
import ImageIO
import UniformTypeIdentifiers
@testable import Photonize

// NOTE: There is no test target yet — slice 12 creates it. These tests are kept
// here alongside the code they cover so they are ready to wire in.
final class ImageExporterTests: XCTestCase {

    /// The folder names must match Windows so a folder organised on one platform
    /// looks the same on the other.
    func testFolderNamesMatchWindows() {
        XCTAssertEqual(ExportFormat.webp.folderName, "WebP")
        XCTAssertEqual(ExportFormat.png.folderName, "PNG")
        XCTAssertEqual(ExportFormat.jpg.folderName, "JPG")
    }

    /// Round-trip a fixture through `WebPEncoder`: the output is non-empty, decodes
    /// via ImageIO, and preserves the source's pixel dimensions. We do not assert
    /// WebP undercuts the source PNG — that is input-dependent (PNG's per-channel
    /// filters can beat lossy WebP on synthetic gradients), so instead we assert the
    /// encoder property that actually holds for any image: lower quality yields a
    /// smaller file.
    func testWebPRoundTrip() throws {
        let width = 256, height = 192
        let cgImage = Self.makeTestImage(width: width, height: height)

        let webp = try WebPEncoder.encode(cgImage, quality: 90)
        XCTAssertFalse(webp.isEmpty)

        let lowQuality = try WebPEncoder.encode(cgImage, quality: 20)
        XCTAssertLessThan(lowQuality.count, webp.count, "lower WebP quality should produce a smaller file")

        guard let src = CGImageSourceCreateWithData(webp as CFData, nil),
              let decoded = CGImageSourceCreateImageAtIndex(src, 0, nil) else {
            return XCTFail("WebP output did not decode via ImageIO")
        }
        XCTAssertEqual(decoded.width, width)
        XCTAssertEqual(decoded.height, height)
    }

    /// Given three existing outputs and `.skipExisting`, exactly the other files
    /// are written and `skipped == 3`.
    func testSkipExistingLeavesExistingUntouched() async throws {
        let dir = try Self.makeTempDir()
        defer { try? FileManager.default.removeItem(at: dir) }

        // Six source PNGs; pre-create three matching outputs in the JPG folder.
        var sources: [PhotoItem] = []
        for i in 0..<6 {
            let url = dir.appendingPathComponent("img\(i).png")
            try Self.encodePNG(Self.makeTestImage(width: 32, height: 32)).write(to: url)
            sources.append(PhotoItem(filePath: url, fileName: url.lastPathComponent, displayOrder: i, isFolder: false, thumbnail: nil))
        }

        let outputFolder = dir.appendingPathComponent("JPG")
        try FileManager.default.createDirectory(at: outputFolder, withIntermediateDirectories: true)
        let existingMarker = Data("stale".utf8)
        for i in 0..<3 {
            try existingMarker.write(to: outputFolder.appendingPathComponent("img\(i).jpg"))
        }

        let exporter = ImageExporter()
        let result = try await exporter.export(
            sources, in: dir, as: .jpg,
            overwrite: { _ in .skipExisting },
            progress: { _, _, _ in }
        )

        XCTAssertEqual(result.skipped, 3)
        XCTAssertEqual(result.exported, 3)
        XCTAssertTrue(result.failed.isEmpty)

        // The three pre-existing outputs must be byte-for-byte untouched.
        for i in 0..<3 {
            let data = try Data(contentsOf: outputFolder.appendingPathComponent("img\(i).jpg"))
            XCTAssertEqual(data, existingMarker, "img\(i).jpg should not have been rewritten")
        }
    }

    /// Cancelling mid-batch leaves `exported < total` and reports no failures.
    func testCancellationLeavesPartialResult() async throws {
        let dir = try Self.makeTempDir()
        defer { try? FileManager.default.removeItem(at: dir) }

        var sources: [PhotoItem] = []
        for i in 0..<200 {
            let url = dir.appendingPathComponent("img\(i).png")
            try Self.encodePNG(Self.makeTestImage(width: 64, height: 64)).write(to: url)
            sources.append(PhotoItem(filePath: url, fileName: url.lastPathComponent, displayOrder: i, isFolder: false, thumbnail: nil))
        }

        let exporter = ImageExporter(maxConcurrent: 2)
        let box = ResultBox()

        let task = Task {
            let result = try await exporter.export(
                sources, in: dir, as: .png,
                overwrite: { _ in .overwriteAll },
                progress: { current, _, _ in
                    // Cancel this task once a few files are through.
                    if current >= 5 { Task { await box.markCancelPoint() } }
                }
            )
            await box.set(result)
        }

        // Let a handful complete, then cancel.
        while await box.cancelPoint == false {
            try await Task.sleep(nanoseconds: 5_000_000)
        }
        task.cancel()
        _ = try? await task.value

        let result = await box.result
        XCTAssertNotNil(result)
        if let result {
            XCTAssertLessThan(result.exported, sources.count, "cancellation should stop before the whole batch")
            XCTAssertTrue(result.failed.isEmpty, "cancellation is not a failure")
        }
    }

    // MARK: - Helpers

    private actor ResultBox {
        private(set) var result: ExportResult?
        private(set) var cancelPoint = false
        func set(_ r: ExportResult) { result = r }
        func markCancelPoint() { cancelPoint = true }
    }

    /// A high-frequency gradient-plus-noise pattern — busy enough that lossy WebP
    /// quality changes move the output size measurably, which the round-trip test
    /// relies on.
    private static func makeTestImage(width: Int, height: Int) -> CGImage {
        let bytesPerRow = width * 4
        var buffer = [UInt8](repeating: 0, count: bytesPerRow * height)
        for y in 0..<height {
            for x in 0..<width {
                let i = y * bytesPerRow + x * 4
                buffer[i]     = UInt8(x &* 255 / max(1, width - 1))
                buffer[i + 1] = UInt8(y &* 255 / max(1, height - 1))
                buffer[i + 2] = UInt8(((x &* 31) ^ (y &* 17)) & 0xFF)
                buffer[i + 3] = 255
            }
        }

        let colorSpace = CGColorSpaceCreateDeviceRGB()
        let bitmapInfo = CGImageAlphaInfo.premultipliedLast.rawValue | CGBitmapInfo.byteOrder32Big.rawValue
        return buffer.withUnsafeMutableBytes { raw in
            let ctx = CGContext(
                data: raw.baseAddress, width: width, height: height, bitsPerComponent: 8,
                bytesPerRow: bytesPerRow, space: colorSpace, bitmapInfo: bitmapInfo
            )!
            return ctx.makeImage()!
        }
    }

    private static func encodePNG(_ image: CGImage) throws -> Data {
        let data = NSMutableData()
        guard let dest = CGImageDestinationCreateWithData(data as CFMutableData, UTType.png.identifier as CFString, 1, nil) else {
            throw WebPEncoderError.encodeFailed
        }
        CGImageDestinationAddImage(dest, image, nil)
        guard CGImageDestinationFinalize(dest) else { throw WebPEncoderError.encodeFailed }
        return data as Data
    }

    private static func makeTempDir() throws -> URL {
        let url = FileManager.default.temporaryDirectory.appendingPathComponent("ImageExporterTests-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: url, withIntermediateDirectories: true)
        return url
    }
}
