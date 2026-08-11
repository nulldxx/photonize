import XCTest
@testable import Photonize

// NOTE: No test target yet (slice 12 adds it). Kept alongside the code for now.
// Mirrors Photonize.Tests/TagTests.cs — the sidecar contract, the pure sort, and
// tags surviving a rename. TagService and TagSorter are the most testable code in
// the app, so they carry the bulk of the assertions.
final class TagTests: XCTestCase {

    private let service = TagService()

    // MARK: - TagService round trip

    func testSaveLoadRoundTripPreservesEveryTag() throws {
        let dir = try makeTempDir()
        defer { try? FileManager.default.removeItem(at: dir) }

        let photos = [
            makePhoto("a.jpg", in: dir, tag: 1),
            makePhoto("b.png", in: dir, tag: 9),
            makePhoto("c.webp", in: dir, tag: 0),
            makePhoto("untagged.jpg", in: dir, tag: nil),
        ]
        service.save(photos, to: dir)

        let loaded = service.load(from: dir)
        XCTAssertEqual(loaded["a.jpg"], 1)
        XCTAssertEqual(loaded["b.png"], 9)
        XCTAssertEqual(loaded["c.webp"], 0)
        XCTAssertNil(loaded["untagged.jpg"], "untagged photos aren't written")
        XCTAssertEqual(loaded.count, 3)
    }

    /// A value outside 0-9 can't be displayed, so it's dropped on load — the file
    /// then reads as untagged for that name.
    func testOutOfRangeValueLoadsAsUntagged() throws {
        let dir = try makeTempDir()
        defer { try? FileManager.default.removeItem(at: dir) }

        let json = "{\n  \"a.jpg\" : 42\n}"
        try json.write(to: TagService.url(for: dir), atomically: true, encoding: .utf8)

        let loaded = service.load(from: dir)
        XCTAssertNil(loaded["a.jpg"])
        XCTAssertTrue(loaded.isEmpty)
    }

    /// Saving with nothing tagged unlinks an existing sidecar rather than writing
    /// `{}` — no empty file left lying around.
    func testSaveWithNoTagsDeletesExistingSidecar() throws {
        let dir = try makeTempDir()
        defer { try? FileManager.default.removeItem(at: dir) }

        service.save([makePhoto("a.jpg", in: dir, tag: 3)], to: dir)
        XCTAssertTrue(FileManager.default.fileExists(atPath: TagService.url(for: dir).path))

        service.save([makePhoto("a.jpg", in: dir, tag: nil)], to: dir)
        XCTAssertFalse(FileManager.default.fileExists(atPath: TagService.url(for: dir).path))
    }

    func testLoadFromDirectoryWithNoSidecarReturnsEmpty() throws {
        let dir = try makeTempDir()
        defer { try? FileManager.default.removeItem(at: dir) }

        XCTAssertTrue(service.load(from: dir).isEmpty)
    }

    // MARK: - TagSorter

    func testSortByTagOrdersFoldersThenDigitsThenUntagged() {
        let photos = [
            makePhoto("z.jpg", displayOrder: 0, tag: nil),
            folder("..", displayOrder: 1),
            makePhoto("b.jpg", displayOrder: 2, tag: 2),
            folder("sub", displayOrder: 3),
            makePhoto("a.jpg", displayOrder: 4, tag: 1),
            makePhoto("c.jpg", displayOrder: 5, tag: 2),
        ]

        let sorted = TagSorter.sortByTag(photos).map { $0.fileName }
        // Folders first ("sub" after ".." by displayOrder), then tag 1, then the
        // two tag-2 photos in stable order, then untagged.
        XCTAssertEqual(sorted, ["..", "sub", "a.jpg", "b.jpg", "c.jpg", "z.jpg"])
    }

    /// ".." keeps the front among the folders because folders share group rank and
    /// break ties on displayOrder.
    func testSortByTagKeepsParentFirstAmongFolders() {
        let photos = [
            folder("sub", displayOrder: 0),
            folder("..", displayOrder: 1),
            makePhoto("a.jpg", displayOrder: 2, tag: 5),
        ]
        // Give ".." the lower displayOrder so it leads.
        var ordered = photos
        ordered[0].displayOrder = 1
        ordered[1].displayOrder = 0

        let sorted = TagSorter.sortByTag(ordered).map { $0.fileName }
        XCTAssertEqual(sorted, ["..", "sub", "a.jpg"])
    }

    func testSortByTagIsIdempotent() {
        let photos = [
            makePhoto("z.jpg", displayOrder: 0, tag: nil),
            folder("..", displayOrder: 1),
            makePhoto("b.jpg", displayOrder: 2, tag: 2),
            makePhoto("a.jpg", displayOrder: 3, tag: 1),
            makePhoto("y.jpg", displayOrder: 4, tag: nil),
            makePhoto("c.jpg", displayOrder: 5, tag: 2),
        ]

        let once = TagSorter.sortByTag(photos)
        let twice = TagSorter.sortByTag(once)
        XCTAssertEqual(once.map { $0.id }, twice.map { $0.id }, "sortByTag must be a fixed point")
    }

    // MARK: - Tags through a rename

    func testTagsSurviveRename() async throws {
        let dir = try makeTempDir()
        defer { try? FileManager.default.removeItem(at: dir) }

        var items = [
            makePhoto("a.jpg", in: dir, displayOrder: 0, tag: 3),
            makePhoto("b.jpg", in: dir, displayOrder: 1, tag: 7),
        ]

        let result = await FileRenamer().renameFiles(items, prefix: "trip")
        XCTAssertTrue(result.success, result.message)

        // Apply the mapping to the in-memory items (as the view model does), then
        // re-save the sidecar under the new names.
        for entry in result.mapping {
            if let i = items.firstIndex(where: { $0.id == entry.id }) {
                items[i].filePath = entry.url
                items[i].fileName = entry.url.lastPathComponent
            }
        }
        service.save(items, to: dir)

        let loaded = service.load(from: dir)
        XCTAssertEqual(loaded["trip-00001.jpg"], 3)
        XCTAssertEqual(loaded["trip-00002.jpg"], 7)
        XCTAssertNil(loaded["a.jpg"], "no stale pre-rename entries")
        XCTAssertNil(loaded["b.jpg"])
        XCTAssertEqual(loaded.count, 2)
    }

    /// A two-file swap (reorder) keeps each tag with its own image, not its slot.
    func testTagsSurviveTwoFileSwap() async throws {
        let dir = try makeTempDir()
        defer { try? FileManager.default.removeItem(at: dir) }

        // a is tagged 3, b is tagged 7. Reorder so b sorts first.
        var items = [
            makePhoto("a.jpg", in: dir, displayOrder: 1, tag: 3),
            makePhoto("b.jpg", in: dir, displayOrder: 0, tag: 7),
        ]

        let result = await FileRenamer().renameFiles(items, prefix: "trip")
        XCTAssertTrue(result.success, result.message)

        for entry in result.mapping {
            if let i = items.firstIndex(where: { $0.id == entry.id }) {
                items[i].filePath = entry.url
                items[i].fileName = entry.url.lastPathComponent
            }
        }
        service.save(items, to: dir)

        let loaded = service.load(from: dir)
        // b became trip-00001 (it sorted first) and keeps tag 7; a became
        // trip-00002 and keeps tag 3.
        XCTAssertEqual(loaded["trip-00001.jpg"], 7)
        XCTAssertEqual(loaded["trip-00002.jpg"], 3)
    }

    // MARK: - Helpers

    private func makeTempDir() throws -> URL {
        let dir = FileManager.default.temporaryDirectory
            .appendingPathComponent("TagTests-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        return dir
    }

    /// A photo whose file exists on disk under `dir`, for the rename tests.
    private func makePhoto(_ name: String, in dir: URL, displayOrder: Int = 0, tag: Int?) -> PhotoItem {
        let url = dir.appendingPathComponent(name)
        try? Data(name.utf8).write(to: url)
        var item = PhotoItem(filePath: url, fileName: name, displayOrder: displayOrder, isFolder: false, thumbnail: nil)
        item.tag = tag
        return item
    }

    /// A photo with no backing file, for the pure-sort tests.
    private func makePhoto(_ name: String, displayOrder: Int, tag: Int?) -> PhotoItem {
        var item = PhotoItem(
            filePath: URL(fileURLWithPath: "/tmp/\(name)"),
            fileName: name,
            displayOrder: displayOrder,
            isFolder: false,
            thumbnail: nil
        )
        item.tag = tag
        return item
    }

    private func folder(_ name: String, displayOrder: Int) -> PhotoItem {
        PhotoItem(
            filePath: URL(fileURLWithPath: "/tmp/\(name)"),
            fileName: name,
            displayOrder: displayOrder,
            isFolder: true,
            thumbnail: nil
        )
    }
}
