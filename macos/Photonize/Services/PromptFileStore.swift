import Foundation

/// Loads, saves and deletes the `.txt` prompt (caption) file that lives beside an
/// image for AI-training workflows (slice 09). `url(for:)` is the single
/// definition of "an image's prompt file" — `SidecarFiles` and slice 10's
/// tag-move defer to `SidecarFiles.captionExtension`, so the `.txt` rule lives in
/// exactly one place.
struct PromptFileStore {
    /// The prompt file for an image: same base name, `.txt` extension, same
    /// directory. Only the last extension is swapped, so a dotted name is handled:
    /// `a/b/photo.webp` → `a/b/photo.txt`, `photo.v2.jpg` → `photo.v2.txt`.
    static func url(for image: URL) -> URL {
        image.deletingPathExtension().appendingPathExtension(SidecarFiles.captionExtension)
    }

    /// The prompt text for `image`, or `""` when no `.txt` exists — an absent file
    /// is a legitimate "no caption yet" state, not an error.
    func load(for image: URL) throws -> String {
        let url = Self.url(for: image)
        guard FileManager.default.fileExists(atPath: url.path) else { return "" }
        return try String(contentsOf: url, encoding: .utf8)
    }

    /// Save `text` beside `image` as UTF-8 (no BOM). Empty or whitespace-only
    /// content deletes any existing file and writes nothing new: a trainer that
    /// sees an empty `.txt` may caption the image as "", so an emptied prompt must
    /// leave no file behind. Whatever line endings the user typed are written
    /// verbatim — the string is not normalised.
    func save(_ text: String, for image: URL) throws {
        let url = Self.url(for: image)
        if text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
            if FileManager.default.fileExists(atPath: url.path) {
                try FileManager.default.removeItem(at: url)
            }
            return
        }
        try text.write(to: url, atomically: true, encoding: .utf8)
    }
}
