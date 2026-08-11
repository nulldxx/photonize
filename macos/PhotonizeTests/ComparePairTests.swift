import XCTest
@testable import Photonize

// NOTE: No test target yet (slice 12 adds it). Kept alongside the code for now.
// These exercise the window-free compare-enablement/pairing helper on the view
// model — the divider view itself is hover-driven and not unit-testable.
final class ComparePairTests: XCTestCase {

    /// Build `count` photos, the first `folders` of them marked as folders, so
    /// index N maps predictably onto a folder/file. `displayOrder` matches the
    /// array index.
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

    // MARK: - Enablement

    func testExactlyTwoFilesEnablesCompare() {
        let photos = makePhotos(count: 5)
        let selection: Set<PhotoItem.ID> = [photos[1].id, photos[3].id]
        XCTAssertNotNil(PhotoGridViewModel.comparePair(in: photos, selection: selection))
    }

    func testOneFileDisablesCompare() {
        let photos = makePhotos(count: 5)
        let selection: Set<PhotoItem.ID> = [photos[2].id]
        XCTAssertNil(PhotoGridViewModel.comparePair(in: photos, selection: selection))
    }

    func testThreeFilesDisablesCompare() {
        let photos = makePhotos(count: 5)
        let selection: Set<PhotoItem.ID> = [photos[0].id, photos[1].id, photos[2].id]
        XCTAssertNil(PhotoGridViewModel.comparePair(in: photos, selection: selection))
    }

    func testSelectionContainingAFolderDisablesCompare() {
        // Two folders followed by three files; select one folder + one file.
        let photos = makePhotos(count: 5, folders: 2)
        let selection: Set<PhotoItem.ID> = [photos[0].id, photos[3].id]
        XCTAssertNil(PhotoGridViewModel.comparePair(in: photos, selection: selection))
    }

    func testTwoFilesPlusAFolderDisablesCompare() {
        // A folder anywhere in the selection disqualifies it, even with two files.
        let photos = makePhotos(count: 5, folders: 1)
        let selection: Set<PhotoItem.ID> = [photos[0].id, photos[2].id, photos[3].id]
        XCTAssertNil(PhotoGridViewModel.comparePair(in: photos, selection: selection))
    }

    // MARK: - Ordering

    func testPairIsOrderedByDisplayOrder() {
        // Select later-then-earlier; A must still be the earlier photo.
        let photos = makePhotos(count: 5)
        let selection: Set<PhotoItem.ID> = [photos[3].id, photos[1].id]
        let pair = PhotoGridViewModel.comparePair(in: photos, selection: selection)
        XCTAssertEqual(pair?.urlA, photos[1].filePath)
        XCTAssertEqual(pair?.urlB, photos[3].filePath)
    }

    func testPairOrderIndependentOfArrayOrderVsDisplayOrder() {
        // Two photos whose displayOrder disagrees with array position: A follows
        // displayOrder, not the array.
        var photos = makePhotos(count: 2)
        photos[0].displayOrder = 5   // array-first but ordered later
        photos[1].displayOrder = 2
        let selection: Set<PhotoItem.ID> = [photos[0].id, photos[1].id]
        let pair = PhotoGridViewModel.comparePair(in: photos, selection: selection)
        XCTAssertEqual(pair?.urlA, photos[1].filePath)
        XCTAssertEqual(pair?.urlB, photos[0].filePath)
    }
}
