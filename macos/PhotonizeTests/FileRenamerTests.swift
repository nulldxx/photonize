import XCTest
@testable import Photonize

// NOTE: No test target yet (slice 12 adds it). Kept alongside the code for now.
// These exercise the naming rule, prefix validation, and the two-pass rename.
final class FileRenamerTests: XCTestCase {

    private let renamer = FileRenamer()

    // MARK: - Prefix validation (nil == accepted)

    func testValidatePrefixRejectsInvalid() {
        XCTAssertNotNil(renamer.validatePrefix(".."), "reserved name")
        XCTAssertNotNil(renamer.validatePrefix("."), "reserved name")
        XCTAssertNotNil(renamer.validatePrefix(".hidden"), "leading dot hides files")
        XCTAssertNotNil(renamer.validatePrefix("a/b"), "path separator")
        XCTAssertNotNil(renamer.validatePrefix("a:b"), "colon")
        XCTAssertNotNil(renamer.validatePrefix("  "), "whitespace only")
        XCTAssertNotNil(renamer.validatePrefix(""), "empty")
        XCTAssertNotNil(renamer.validatePrefix("trip "), "trailing space")
        XCTAssertNotNil(renamer.validatePrefix(" trip"), "leading space")
    }

    func testValidatePrefixAcceptsValid() {
        XCTAssertNil(renamer.validatePrefix("holiday-2026"))
        XCTAssertNil(renamer.validatePrefix("wedding_day"))
        XCTAssertNil(renamer.validatePrefix("Photos 2026"))
    }

    // MARK: - Naming rule

    func testTargetNameIsOneBased() {
        XCTAssertEqual(renamer.targetName(prefix: "trip", index: 0, extension: "jpg"), "trip-00001.jpg")
        XCTAssertEqual(renamer.targetName(prefix: "trip", index: 41, extension: "png"), "trip-00042.png")
    }

    // MARK: - Two-pass rename round trip

    /// Every input id maps to a URL that exists on disk after the rename.
    func testRenameMapsEveryIdToAnExistingFile() async throws {
        let dir = try makeTempDir()
        defer { try? FileManager.default.removeItem(at: dir) }

        let items = try makeFiles(named: ["c.jpg", "a.jpg", "b.jpg"], in: dir)

        let result = await renamer.renameFiles(items, prefix: "trip")

        XCTAssertTrue(result.success, result.message)
        XCTAssertEqual(result.mapping.count, items.count)
        for entry in result.mapping {
            XCTAssertTrue(FileManager.default.fileExists(atPath: entry.url.path),
                          "expected \(entry.url.lastPathComponent) on disk")
        }
        // Names follow display order: c=0, a=1, b=2.
        XCTAssertEqual(url(for: items[0].id, in: result).lastPathComponent, "trip-00001.jpg")
        XCTAssertEqual(url(for: items[1].id, in: result).lastPathComponent, "trip-00002.jpg")
        XCTAssertEqual(url(for: items[2].id, in: result).lastPathComponent, "trip-00003.jpg")
    }

    /// The swap case (a→b, b→a) is exactly why the two-pass algorithm exists:
    /// a naive single pass would clobber one file.
    func testRenameHandlesSwap() async throws {
        let dir = try makeTempDir()
        defer { try? FileManager.default.removeItem(at: dir) }

        // Files already named in the target scheme, but reordered so each must
        // take the other's current name.
        var items = try makeFiles(named: ["trip-00001.jpg", "trip-00002.jpg"], in: dir)
        items[0].displayOrder = 1  // trip-00001 -> trip-00002
        items[1].displayOrder = 0  // trip-00002 -> trip-00001

        let result = await renamer.renameFiles(items, prefix: "trip")

        XCTAssertTrue(result.success, result.message)
        XCTAssertEqual(url(for: items[0].id, in: result).lastPathComponent, "trip-00002.jpg")
        XCTAssertEqual(url(for: items[1].id, in: result).lastPathComponent, "trip-00001.jpg")
        // Both survive — no file was clobbered.
        XCTAssertTrue(FileManager.default.fileExists(atPath: dir.appendingPathComponent("trip-00001.jpg").path))
        XCTAssertTrue(FileManager.default.fileExists(atPath: dir.appendingPathComponent("trip-00002.jpg").path))
    }

    // MARK: - Helpers

    private func makeTempDir() throws -> URL {
        let dir = FileManager.default.temporaryDirectory
            .appendingPathComponent("FileRenamerTests-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        return dir
    }

    private func makeFiles(named names: [String], in dir: URL) throws -> [PhotoItem] {
        try names.enumerated().map { index, name in
            let url = dir.appendingPathComponent(name)
            try Data(name.utf8).write(to: url)
            return PhotoItem(filePath: url, fileName: name, displayOrder: index, isFolder: false, thumbnail: nil)
        }
    }

    private func url(for id: PhotoItem.ID, in result: RenameResult) -> URL {
        result.mapping.first(where: { $0.id == id })?.url ?? URL(fileURLWithPath: "/nonexistent")
    }
}
