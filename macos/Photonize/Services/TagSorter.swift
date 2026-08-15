import Foundation

/// Pure ordering logic for "Sort by tag". Kept free of file I/O and SwiftUI so it
/// can be exercised directly by unit tests — a verbatim port of `TagSorter.cs`.
enum TagSorter {
    /// Returns the photos reordered so that folders (including the ".." parent)
    /// stay at the front, followed by the photos grouped by digit in ascending
    /// order. Untagged photos sort as though their tag were 5.5, so they land
    /// between tag 5 and tag 6. Ordering within every group is stable
    /// (Swift's `sorted(by:)` is guaranteed stable) on `displayOrder`, so `..`
    /// sorts first among the folders and `sortByTag(sortByTag(x)) == sortByTag(x)`.
    static func sortByTag(_ photos: [PhotoItem]) -> [PhotoItem] {
        photos.sorted { lhs, rhs in
            let lRank = groupRank(lhs), rRank = groupRank(rhs)
            if lRank != rRank { return lRank < rRank }

            let lTag = tagRank(lhs), rTag = tagRank(rhs)
            if lTag != rTag { return lTag < rTag }

            return lhs.displayOrder < rhs.displayOrder
        }
    }

    // 0 = folders (".." sorts first within this group via displayOrder),
    // 1 = photos, tagged or not.
    private static func groupRank(_ photo: PhotoItem) -> Int {
        photo.isFolder ? 0 : 1
    }

    // Tags are doubled so the untagged slot (11) sits exactly halfway between
    // tag 5 (10) and tag 6 (12) without resorting to floating point.
    private static func tagRank(_ photo: PhotoItem) -> Int {
        guard let tag = photo.tag else { return 11 }
        return tag * 2
    }
}
