import XCTest
@testable import Photonize

// NOTE: No test target yet (slice 12 adds it). Kept alongside the code for now.
// These exercise the window-free selection/navigation helpers on the view model.
final class GridSelectionTests: XCTestCase {

    /// Build `count` photos, the first `folders` of them marked as folders, so
    /// index N maps predictably onto a folder/file.
    private func makePhotos(count: Int, folders: Int = 0) -> [PhotoItem] {
        (0..<count).map { i in
            PhotoItem(
                filePath: URL(fileURLWithPath: "/tmp/item-\(i)"),
                fileName: "item-\(i)",
                displayOrder: i,
                isFolder: i < folders,
                thumbnail: nil
            )
        }
    }

    // MARK: - Range selection

    func testRangeSelectionInclusiveForwards() {
        let photos = makePhotos(count: 10)
        let ids = PhotoGridViewModel.rangeSelection(in: photos, from: 2, to: 7)
        XCTAssertEqual(ids, Set(photos[2...7].map { $0.id }))
        XCTAssertEqual(ids.count, 6)
    }

    func testRangeSelectionInclusiveBackwards() {
        let photos = makePhotos(count: 10)
        // Anchor at 2, click 0 -> exactly 0...2.
        let ids = PhotoGridViewModel.rangeSelection(in: photos, from: 2, to: 0)
        XCTAssertEqual(ids, Set(photos[0...2].map { $0.id }))
        XCTAssertEqual(ids.count, 3)
    }

    func testRangeSelectionClampsOutOfBounds() {
        let photos = makePhotos(count: 4)
        let ids = PhotoGridViewModel.rangeSelection(in: photos, from: -5, to: 99)
        XCTAssertEqual(ids, Set(photos.map { $0.id }))
    }

    // MARK: - Select all (files only)

    func testFileIDsExcludeFolders() {
        // Three folders followed by five files.
        let photos = makePhotos(count: 8, folders: 3)
        let ids = PhotoGridViewModel.fileIDs(in: photos)
        XCTAssertEqual(ids.count, 5)
        XCTAssertEqual(ids, photos[3...7].map { $0.id })
    }

    // MARK: - Row-wrapping focus movement

    func testRightWrapsToNextRow() {
        // 4 columns; index 3 is the last of row 0, so -> lands on 4 (first of row 1).
        XCTAssertEqual(
            PhotoGridViewModel.nextFocusIndex(from: 3, direction: .right, columnCount: 4, count: 12),
            4
        )
    }

    func testLeftWrapsToPreviousRow() {
        XCTAssertEqual(
            PhotoGridViewModel.nextFocusIndex(from: 4, direction: .left, columnCount: 4, count: 12),
            3
        )
    }

    func testRightAtLastItemIsNoOp() {
        XCTAssertNil(
            PhotoGridViewModel.nextFocusIndex(from: 11, direction: .right, columnCount: 4, count: 12)
        )
    }

    func testLeftAtFirstItemIsNoOp() {
        XCTAssertNil(
            PhotoGridViewModel.nextFocusIndex(from: 0, direction: .left, columnCount: 4, count: 12)
        )
    }

    func testDownJumpsOneRow() {
        XCTAssertEqual(
            PhotoGridViewModel.nextFocusIndex(from: 1, direction: .down, columnCount: 4, count: 12),
            5
        )
    }

    func testDownOffTheBottomIsNoOp() {
        // index 10 + 4 = 14 >= 12, so no row below.
        XCTAssertNil(
            PhotoGridViewModel.nextFocusIndex(from: 10, direction: .down, columnCount: 4, count: 12)
        )
    }

    func testUpJumpsOneRow() {
        XCTAssertEqual(
            PhotoGridViewModel.nextFocusIndex(from: 5, direction: .up, columnCount: 4, count: 12),
            1
        )
    }

    func testUpFromTopRowIsNoOp() {
        XCTAssertNil(
            PhotoGridViewModel.nextFocusIndex(from: 2, direction: .up, columnCount: 4, count: 12)
        )
    }
}
