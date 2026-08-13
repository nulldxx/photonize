import Foundation

/// Which end of the list an item is being sent to.
enum SendToEdge {
    case front
    case back
}

/// Pure list-position arithmetic for "Send to ▸ Front / Back". Kept free of file
/// I/O and SwiftUI so it can be exercised directly by unit tests — the mirror of
/// `ListReorder.cs` on Windows.
enum ListReorder {
    /// Returns a new ordering with the given items collected at one end of the
    /// list. Folder tiles (including the ".." parent) are never moved and always
    /// keep their leading block, so "front" means the first *media* slot rather
    /// than index 0. The relative order of the moved items is preserved.
    static func sendTo(_ photos: [PhotoItem], moving: [PhotoItem.ID], to edge: SendToEdge) -> [PhotoItem] {
        guard !moving.isEmpty else { return photos }

        // Folders anchor the top of the grid; they are never a legal move target.
        let movingSet = Set(
            photos.filter { moving.contains($0.id) && !$0.isFolder }.map { $0.id }
        )
        guard !movingSet.isEmpty else { return photos }

        // Keep the moved items in the order they appear now, not the order they
        // happened to be selected in.
        let moved = photos.filter { movingSet.contains($0.id) }
        var remainder = photos.filter { !movingSet.contains($0.id) }

        let insertAt = edge == .front ? firstMediaIndex(remainder) : remainder.count
        remainder.insert(contentsOf: moved, at: insertAt)
        return remainder
    }

    /// True when `sendTo` would actually change the ordering, so the menu entries
    /// can disable themselves rather than arm a spurious unsaved-changes guard.
    static func wouldChangeOrder(_ photos: [PhotoItem], moving: [PhotoItem.ID], to edge: SendToEdge) -> Bool {
        guard !moving.isEmpty else { return false }
        return sendTo(photos, moving: moving, to: edge).map({ $0.id }) != photos.map({ $0.id })
    }

    /// Index of the first non-folder tile — the slot "front" targets. Load order
    /// puts the ".." tile first, then folders, then media, so this is the end of
    /// that leading block (or the count when there is no media at all).
    static func firstMediaIndex(_ photos: [PhotoItem]) -> Int {
        photos.firstIndex(where: { !$0.isFolder }) ?? photos.count
    }
}
