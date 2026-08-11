import XCTest
@testable import Photonize

// NOTE: No test target yet (slice 12 adds it). Kept alongside the code for now.
// These exercise the pure position maths, source sanitising, and a full import
// round trip on disk.
final class PhotoImporterTests: XCTestCase {

    // MARK: - distribute

    /// 3 new photos into 9 existing interleave at positions 3, 6, 9 and both
    /// groups keep their relative order.
    func testDistributeThreeIntoNine() {
        let existing = (0..<9).map { URL(fileURLWithPath: "/existing/e\($0).jpg") }
        let newFiles = (0..<3).map { URL(fileURLWithPath: "/new/n\($0).jpg") }

        let ops = PhotoImporter.distributePositions(existing: existing, newFiles: newFiles)
            .sorted { $0.finalIndex < $1.finalIndex }

        XCTAssertEqual(ops.count, 12)
        let newIndices = ops.filter { $0.isNew }.map { $0.finalIndex }
        XCTAssertEqual(newIndices, [3, 6, 9])

        // Relative order within each group is preserved.
        XCTAssertEqual(ops.filter { $0.isNew }.map { $0.source }, newFiles)
        XCTAssertEqual(ops.filter { !$0.isNew }.map { $0.source }, existing)
    }

    // MARK: - insert

    func testInsertAtZeroPrepends() {
        let existing = [URL(fileURLWithPath: "/e/a.jpg"), URL(fileURLWithPath: "/e/b.jpg")]
        let newFiles = [URL(fileURLWithPath: "/n/x.jpg")]

        let ops = PhotoImporter.insertPositions(existing: existing, newFiles: newFiles, insertIndex: 0)
            .sorted { $0.finalIndex < $1.finalIndex }

        XCTAssertEqual(ops.map { $0.source }, [newFiles[0], existing[0], existing[1]])
        XCTAssertEqual(ops.map { $0.finalIndex }, [0, 1, 2])
    }

    func testInsertAtCountAppends() {
        let existing = [URL(fileURLWithPath: "/e/a.jpg"), URL(fileURLWithPath: "/e/b.jpg")]
        let newFiles = [URL(fileURLWithPath: "/n/x.jpg")]

        let ops = PhotoImporter.insertPositions(existing: existing, newFiles: newFiles, insertIndex: existing.count)
            .sorted { $0.finalIndex < $1.finalIndex }

        XCTAssertEqual(ops.map { $0.source }, [existing[0], existing[1], newFiles[0]])
        XCTAssertEqual(ops.map { $0.finalIndex }, [0, 1, 2])
    }

    // MARK: - sanitising

    func testSourcesAlreadyInTargetAreDropped() {
        let target = URL(fileURLWithPath: "/photos/trip")
        let sources = [
            URL(fileURLWithPath: "/photos/trip/already.jpg"),   // already in target
            URL(fileURLWithPath: "/downloads/new.jpg"),
        ]

        let result = PhotoImporter.sanitizedSources(sources, into: target)

        XCTAssertEqual(result, [URL(fileURLWithPath: "/downloads/new.jpg")])
    }

    func testDuplicateSourcesAreDeduplicated() {
        let target = URL(fileURLWithPath: "/photos/trip")
        let dupe = URL(fileURLWithPath: "/downloads/a.jpg")
        let sources = [dupe, dupe, URL(fileURLWithPath: "/downloads/b.jpg")]

        let result = PhotoImporter.sanitizedSources(sources, into: target)

        XCTAssertEqual(result, [dupe, URL(fileURLWithPath: "/downloads/b.jpg")])
    }

    // MARK: - full round trip

    /// Importing ends with a contiguous prefix-00001…N sequence, leaves the
    /// originals where they were, and drops no temp files.
    func testImportRoundTripRenumbersAndKeepsOriginals() async throws {
        let targetDir = try makeTempDir()
        let sourceDir = try makeTempDir()
        defer {
            try? FileManager.default.removeItem(at: targetDir)
            try? FileManager.default.removeItem(at: sourceDir)
        }

        let existing = try makeItems(named: ["a.jpg", "b.jpg", "c.jpg"], in: targetDir)
        let sources = try makeFiles(named: ["x.jpg", "y.jpg"], in: sourceDir)

        let importer = PhotoImporter()
        let result = try await importer.importPhotos(
            sources,
            into: targetDir,
            existing: existing,
            prefix: "trip",
            mode: .insert(at: 1),
            progress: { _, _, _ in }
        )

        XCTAssertEqual(result.count, 5)

        // Contiguous trip-00001…trip-00005 on disk.
        for i in 1...5 {
            let name = String(format: "trip-%05d.jpg", i)
            XCTAssertTrue(
                FileManager.default.fileExists(atPath: targetDir.appendingPathComponent(name).path),
                "expected \(name)"
            )
        }

        // Originals untouched (copied, not moved).
        for source in sources {
            XCTAssertTrue(FileManager.default.fileExists(atPath: source.path),
                          "source \(source.lastPathComponent) should still exist")
        }

        // No temp leftovers of either kind.
        let leftovers = try FileManager.default.contentsOfDirectory(atPath: targetDir.path)
            .filter { $0.contains(".photonize-import-") || $0.contains(".tmp_rename") }
        XCTAssertTrue(leftovers.isEmpty, "unexpected temp files: \(leftovers)")
    }

    // MARK: - Helpers

    private func makeTempDir() throws -> URL {
        let dir = FileManager.default.temporaryDirectory
            .appendingPathComponent("PhotoImporterTests-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        return dir
    }

    private func makeFiles(named names: [String], in dir: URL) throws -> [URL] {
        try names.map { name in
            let url = dir.appendingPathComponent(name)
            try Data(name.utf8).write(to: url)
            return url
        }
    }

    private func makeItems(named names: [String], in dir: URL) throws -> [PhotoItem] {
        try makeFiles(named: names, in: dir).enumerated().map { index, url in
            PhotoItem(filePath: url, fileName: url.lastPathComponent, displayOrder: index, isFolder: false, thumbnail: nil)
        }
    }
}
