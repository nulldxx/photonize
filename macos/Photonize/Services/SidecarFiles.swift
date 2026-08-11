import Foundation

/// The rule that an image and its companion files travel together. A photo's
/// caption lives in a `.txt` file with the same base name (slice 09 writes
/// these); when the image moves, is renamed, or is deleted, the caption must
/// follow. Keeping that rule in one place means slice 06 (move-to-folder),
/// slice 09 (captions) and slice 10 (move-tagged-to-subfolders) all agree on
/// what "companions" means rather than each re-deriving it.
enum SidecarFiles {
    /// The caption sidecar extension. A photo "trip-00001.webp" pairs with
    /// "trip-00001.txt" in the same directory.
    static let captionExtension = "txt"

    /// Companion files that exist on disk alongside `imageURL`. Currently just
    /// the caption `.txt`; adding a kind here makes every caller carry it.
    static func companions(of imageURL: URL) -> [URL] {
        let candidate = imageURL.deletingPathExtension().appendingPathExtension(captionExtension)
        return FileManager.default.fileExists(atPath: candidate.path) ? [candidate] : []
    }

    /// Move every companion of `imageURL` into `destinationDir`, keeping each
    /// companion's own name. Called after the image itself has moved, so the
    /// caption lands next to it. An existing file of the same name in the target
    /// is replaced, matching how the image move resolves its own collision.
    static func moveCompanions(of imageURL: URL, toDirectory destinationDir: URL) throws {
        for companion in companions(of: imageURL) {
            let dest = destinationDir.appendingPathComponent(companion.lastPathComponent)
            if FileManager.default.fileExists(atPath: dest.path) {
                try FileManager.default.removeItem(at: dest)
            }
            try FileManager.default.moveItem(at: companion, to: dest)
        }
    }
}
