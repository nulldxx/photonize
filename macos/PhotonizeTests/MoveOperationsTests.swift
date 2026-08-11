import XCTest
@testable import Photonize

// NOTE: No test target yet (slice 12 adds it). Kept alongside the code for now.
// These cover the move guards that are pure enough to test without driving the
// full view model (which shows AppKit alerts): the descendant guard and the
// caption-sidecar rule.
final class MoveOperationsTests: XCTestCase {

    // MARK: - folder-into-descendant guard

    func testMoveFolderIntoItselfIsRefused() {
        let folder = URL(fileURLWithPath: "/photos/trip")
        XCTAssertTrue(PhotoGridViewModel.isMoveIntoSelfOrDescendant(source: folder, target: folder))
    }

    func testMoveFolderIntoOwnDescendantIsRefused() {
        let folder = URL(fileURLWithPath: "/photos/trip")
        let descendant = URL(fileURLWithPath: "/photos/trip/day1")
        XCTAssertTrue(PhotoGridViewModel.isMoveIntoSelfOrDescendant(source: folder, target: descendant))
    }

    func testMoveFolderIntoSiblingIsAllowed() {
        let folder = URL(fileURLWithPath: "/photos/trip")
        let sibling = URL(fileURLWithPath: "/photos/other")
        XCTAssertFalse(PhotoGridViewModel.isMoveIntoSelfOrDescendant(source: folder, target: sibling))
    }

    /// A prefix match on the path string must respect the path separator:
    /// "/photos/trip" is not a descendant of "/photos/tr".
    func testDescendantGuardRespectsPathBoundaries() {
        let folder = URL(fileURLWithPath: "/photos/tr")
        let notDescendant = URL(fileURLWithPath: "/photos/trip")
        XCTAssertFalse(PhotoGridViewModel.isMoveIntoSelfOrDescendant(source: folder, target: notDescendant))
    }

    // MARK: - caption sidecar travels with the image

    func testMovingAnImageMovesItsTxtSidecar() throws {
        let sourceDir = try makeTempDir()
        let targetDir = try makeTempDir()
        defer {
            try? FileManager.default.removeItem(at: sourceDir)
            try? FileManager.default.removeItem(at: targetDir)
        }

        let image = sourceDir.appendingPathComponent("photo.webp")
        let caption = sourceDir.appendingPathComponent("photo.txt")
        try Data("img".utf8).write(to: image)
        try Data("a caption".utf8).write(to: caption)

        // Move the image itself, then its companions (the order moveItems uses).
        let movedImage = targetDir.appendingPathComponent("photo.webp")
        try FileManager.default.moveItem(at: image, to: movedImage)
        try SidecarFiles.moveCompanions(of: image, toDirectory: targetDir)

        let movedCaption = targetDir.appendingPathComponent("photo.txt")
        XCTAssertTrue(FileManager.default.fileExists(atPath: movedCaption.path), "caption should have moved")
        XCTAssertFalse(FileManager.default.fileExists(atPath: caption.path), "caption should be gone from source")
        XCTAssertEqual(try String(contentsOf: movedCaption, encoding: .utf8), "a caption")
    }

    func testMovingAnImageWithoutSidecarIsANoop() throws {
        let sourceDir = try makeTempDir()
        let targetDir = try makeTempDir()
        defer {
            try? FileManager.default.removeItem(at: sourceDir)
            try? FileManager.default.removeItem(at: targetDir)
        }

        let image = sourceDir.appendingPathComponent("photo.webp")
        try Data("img".utf8).write(to: image)

        // No companions exist; this must not throw.
        XCTAssertNoThrow(try SidecarFiles.moveCompanions(of: image, toDirectory: targetDir))
        XCTAssertTrue(SidecarFiles.companions(of: image).isEmpty)
    }

    // MARK: - Helpers

    private func makeTempDir() throws -> URL {
        let dir = FileManager.default.temporaryDirectory
            .appendingPathComponent("MoveOperationsTests-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        return dir
    }
}
