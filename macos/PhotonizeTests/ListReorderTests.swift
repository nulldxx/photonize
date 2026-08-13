import XCTest
@testable import Photonize

/// Mirrors `Photonize.Tests/ListReorderTests.cs` — the pure "Send to ▸ Front/Back"
/// arithmetic: the folder floor that makes "front" the first *media* slot, the
/// multi-select relative-order rule, and the already-at-the-edge no-op.
final class ListReorderTests: XCTestCase {

    private func photo(_ name: String, order: Int) -> PhotoItem {
        PhotoItem(
            filePath: URL(fileURLWithPath: "/photos/\(name)"),
            fileName: name,
            displayOrder: order,
            isFolder: false,
            thumbnail: nil
        )
    }

    private func folder(_ name: String, order: Int) -> PhotoItem {
        PhotoItem(
            filePath: URL(fileURLWithPath: "/photos/\(name)"),
            fileName: name,
            displayOrder: order,
            isFolder: true,
            thumbnail: nil
        )
    }

    private func names(_ items: [PhotoItem]) -> [String] { items.map { $0.fileName } }

    func testSendToFrontMovesItemToStartOfList() {
        let a = photo("a.jpg", order: 0)
        let b = photo("b.jpg", order: 1)
        let c = photo("c.jpg", order: 2)

        let result = ListReorder.sendTo([a, b, c], moving: [c.id], to: .front)

        XCTAssertEqual(names(result), ["c.jpg", "a.jpg", "b.jpg"])
    }

    func testSendToFrontLandsAfterTheParentAndFolderTiles() {
        let parent = folder("..", order: 0)
        let sub = folder("holiday", order: 1)
        let a = photo("a.jpg", order: 2)
        let b = photo("b.jpg", order: 3)

        let result = ListReorder.sendTo([parent, sub, a, b], moving: [b.id], to: .front)

        XCTAssertEqual(names(result), ["..", "holiday", "b.jpg", "a.jpg"])
    }

    func testSendToBackMovesItemToEndOfList() {
        let a = photo("a.jpg", order: 0)
        let b = photo("b.jpg", order: 1)
        let c = photo("c.jpg", order: 2)

        let result = ListReorder.sendTo([a, b, c], moving: [a.id], to: .back)

        XCTAssertEqual(names(result), ["b.jpg", "c.jpg", "a.jpg"])
    }

    func testMultiSelectKeepsRelativeOrderAtTheFront() {
        let parent = folder("..", order: 0)
        let a = photo("a.jpg", order: 1)
        let b = photo("b.jpg", order: 2)
        let c = photo("c.jpg", order: 3)
        let d = photo("d.jpg", order: 4)

        // Deliberately handed over in reverse selection order.
        let result = ListReorder.sendTo([parent, a, b, c, d], moving: [d.id, b.id], to: .front)

        XCTAssertEqual(names(result), ["..", "b.jpg", "d.jpg", "a.jpg", "c.jpg"])
    }

    func testMultiSelectKeepsRelativeOrderAtTheBack() {
        let a = photo("a.jpg", order: 0)
        let b = photo("b.jpg", order: 1)
        let c = photo("c.jpg", order: 2)
        let d = photo("d.jpg", order: 3)

        let result = ListReorder.sendTo([a, b, c, d], moving: [c.id, a.id], to: .back)

        XCTAssertEqual(names(result), ["b.jpg", "d.jpg", "a.jpg", "c.jpg"])
    }

    func testFolderTilesAreNeverMoved() {
        let parent = folder("..", order: 0)
        let sub = folder("holiday", order: 1)
        let a = photo("a.jpg", order: 2)
        let photos = [parent, sub, a]

        XCTAssertEqual(names(ListReorder.sendTo(photos, moving: [sub.id], to: .front)),
                       ["..", "holiday", "a.jpg"])
        XCTAssertEqual(names(ListReorder.sendTo(photos, moving: [parent.id], to: .back)),
                       ["..", "holiday", "a.jpg"])
    }

    func testMixedSelectionMovesOnlyTheMedia() {
        let parent = folder("..", order: 0)
        let a = photo("a.jpg", order: 1)
        let b = photo("b.jpg", order: 2)

        let result = ListReorder.sendTo([parent, a, b], moving: [parent.id, b.id], to: .front)

        XCTAssertEqual(names(result), ["..", "b.jpg", "a.jpg"])
    }

    func testAlreadyAtTheEdgeIsANoOp() {
        let parent = folder("..", order: 0)
        let a = photo("a.jpg", order: 1)
        let b = photo("b.jpg", order: 2)
        let photos = [parent, a, b]

        XCTAssertEqual(names(ListReorder.sendTo(photos, moving: [a.id], to: .front)),
                       ["..", "a.jpg", "b.jpg"])
        XCTAssertEqual(names(ListReorder.sendTo(photos, moving: [b.id], to: .back)),
                       ["..", "a.jpg", "b.jpg"])

        XCTAssertFalse(ListReorder.wouldChangeOrder(photos, moving: [a.id], to: .front))
        XCTAssertFalse(ListReorder.wouldChangeOrder(photos, moving: [b.id], to: .back))
        XCTAssertTrue(ListReorder.wouldChangeOrder(photos, moving: [b.id], to: .front))
    }

    func testWouldChangeOrderIsFalseForFolderOnlyAndEmptySelections() {
        let parent = folder("..", order: 0)
        let a = photo("a.jpg", order: 1)
        let photos = [parent, a]

        XCTAssertFalse(ListReorder.wouldChangeOrder(photos, moving: [parent.id], to: .back))
        XCTAssertFalse(ListReorder.wouldChangeOrder(photos, moving: [], to: .front))
    }

    func testSendToIsIdempotent() {
        let parent = folder("..", order: 0)
        let a = photo("a.jpg", order: 1)
        let b = photo("b.jpg", order: 2)
        let c = photo("c.jpg", order: 3)
        let photos = [parent, a, b, c]

        let once = ListReorder.sendTo(photos, moving: [c.id], to: .front)
        let twice = ListReorder.sendTo(once, moving: [c.id], to: .front)

        XCTAssertEqual(names(once), names(twice))
        XCTAssertFalse(ListReorder.wouldChangeOrder(once, moving: [c.id], to: .front))
    }

    func testFirstMediaIndexIsCountWhenThereIsNoMedia() {
        let photos = [folder("..", order: 0), folder("holiday", order: 1)]

        XCTAssertEqual(ListReorder.firstMediaIndex(photos), 2)
    }
}
