import Foundation

/// Pure ordering logic for "Sort by tag". Kept free of file I/O and SwiftUI so it
/// can be exercised directly by unit tests — a verbatim port of `TagSorter.cs`.
enum TagSorter {
    /// Returns the photos reordered so that folders (including the ".." parent)
    /// stay at the front, followed by tagged photos grouped by digit in ascending
    /// order, followed by untagged photos. Ordering within every group is stable
    /// (Swift's `sorted(by:)` is guaranteed stable) on `displayOrder`, so `..`
    /// sorts first among the folders and `sortByTag(sortByTag(x)) == sortByTag(x)`.
    static func sortByTag(_ photos: [PhotoItem]) -> [PhotoItem] {
        photos.sorted { lhs, rhs in
            let lRank = groupRank(lhs), rRank = groupRank(rhs)
            if lRank != rRank { return lRank < rRank }

            let lTag = lhs.tag ?? 0, rTag = rhs.tag ?? 0
            if lTag != rTag { return lTag < rTag }

            return lhs.displayOrder < rhs.displayOrder
        }
    }

    // 0 = folders (".." sorts first within this group via displayOrder),
    // 1 = tagged photos, 2 = untagged photos.
    private static func groupRank(_ photo: PhotoItem) -> Int {
        if photo.isFolder { return 0 }
        return photo.tag != nil ? 1 : 2
    }
}
