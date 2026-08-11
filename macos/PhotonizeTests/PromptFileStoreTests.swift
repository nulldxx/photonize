import XCTest
@testable import Photonize

// NOTE: No test target yet (slice 12 adds it). Kept alongside the code for now.
// These exercise the canonical .txt path and the load/save/delete-on-empty rules.
final class PromptFileStoreTests: XCTestCase {

    private let store = PromptFileStore()

    // MARK: - url(for:)

    func testUrlSwapsExtensionToTxt() {
        let image = URL(fileURLWithPath: "/a/b/photo.webp")
        XCTAssertEqual(PromptFileStore.url(for: image).path, "/a/b/photo.txt")
    }

    /// Only the last extension is replaced, so a dotted base name survives.
    func testUrlHandlesDottedNames() {
        let image = URL(fileURLWithPath: "/a/b/photo.v2.jpg")
        XCTAssertEqual(PromptFileStore.url(for: image).path, "/a/b/photo.v2.txt")
    }

    // MARK: - load

    func testLoadReturnsEmptyForMissingFile() throws {
        let dir = try makeTempDir()
        defer { try? FileManager.default.removeItem(at: dir) }

        let image = dir.appendingPathComponent("photo.jpg")
        XCTAssertEqual(try store.load(for: image), "")
    }

    func testLoadRoundTripsUtf8() throws {
        let dir = try makeTempDir()
        defer { try? FileManager.default.removeItem(at: dir) }

        let image = dir.appendingPathComponent("photo.jpg")
        let text = "a café façade — 🌅 with accents"
        try store.save(text, for: image)
        XCTAssertEqual(try store.load(for: image), text)
    }

    // MARK: - save (delete-on-empty)

    func testSaveEmptyDeletesExistingFile() throws {
        let dir = try makeTempDir()
        defer { try? FileManager.default.removeItem(at: dir) }

        let image = dir.appendingPathComponent("photo.jpg")
        try store.save("a caption", for: image)
        XCTAssertTrue(FileManager.default.fileExists(atPath: PromptFileStore.url(for: image).path))

        try store.save("", for: image)
        XCTAssertFalse(FileManager.default.fileExists(atPath: PromptFileStore.url(for: image).path))
    }

    func testSaveWhitespaceOnlyDeletesExistingFile() throws {
        let dir = try makeTempDir()
        defer { try? FileManager.default.removeItem(at: dir) }

        let image = dir.appendingPathComponent("photo.jpg")
        try store.save("a caption", for: image)

        try store.save("   \n ", for: image)
        XCTAssertFalse(FileManager.default.fileExists(atPath: PromptFileStore.url(for: image).path))
    }

    func testSaveEmptyCreatesNothingWhenAbsent() throws {
        let dir = try makeTempDir()
        defer { try? FileManager.default.removeItem(at: dir) }

        let image = dir.appendingPathComponent("photo.jpg")
        try store.save("   \n ", for: image)
        XCTAssertFalse(FileManager.default.fileExists(atPath: PromptFileStore.url(for: image).path))
    }

    // MARK: - Helpers

    private func makeTempDir() throws -> URL {
        let dir = FileManager.default.temporaryDirectory
            .appendingPathComponent("PromptFileStoreTests-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        return dir
    }
}
