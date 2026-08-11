import Foundation

/// Persists quick-tags for a photo directory in a sidecar JSON file.
///
/// This is a shared on-disk contract with the Windows client
/// (`Photonize/Services/TagService.cs`): the file name, the filename-as-key rule,
/// the 0-9 range clamp and the "delete when empty" behaviour must all match, or a
/// folder tagged on one platform reads wrong on the other. Entries are keyed by
/// filename rather than full path so the folder can be moved or copied and keep
/// its tags.
struct TagService {
    static let fileName = ".photonize-tags.json"

    static func url(for directory: URL) -> URL {
        directory.appendingPathComponent(fileName)
    }

    /// Load the filename→digit map for `directory`. A missing, unreadable or
    /// malformed sidecar all return an empty map rather than throwing — an absent
    /// file is the normal "nothing tagged" state. Values outside 0-9 are dropped
    /// so a hand-edited file can't produce a badge that can't be displayed.
    /// Keys are returned verbatim (original case); callers match them
    /// case-insensitively, mirroring the Windows `OrdinalIgnoreCase` dictionary.
    func load(from directory: URL) -> [String: Int] {
        let url = Self.url(for: directory)
        guard let data = try? Data(contentsOf: url),
              let loaded = try? JSONDecoder().decode([String: Int].self, from: data) else {
            return [:]
        }
        return loaded.filter { (0...9).contains($0.value) }
    }

    /// Persist the tags carried by `photos` into `directory`'s sidecar. Folders
    /// and untagged photos are skipped. Keys are the photos' filenames verbatim —
    /// case is never normalised, so a folder round-tripped between the two clients
    /// doesn't churn.
    func save(_ photos: [PhotoItem], to directory: URL) {
        var tags: [String: Int] = [:]
        for photo in photos where !photo.isFolder {
            if let tag = photo.tag {
                tags[photo.fileName] = tag
            }
        }
        write(tags, to: directory)
    }

    /// Write a raw filename→digit map to `directory`'s sidecar, or delete the file
    /// when the map is empty (rather than leaving a `{}` lying around, matching
    /// Windows). Pretty-printed with sorted keys so it diffs cleanly in git.
    /// Shared by `save(_:to:)` and the move-to-subfolders flow, which composes a
    /// destination map directly.
    func write(_ tags: [String: Int], to directory: URL) {
        let url = Self.url(for: directory)

        guard !tags.isEmpty else {
            if FileManager.default.fileExists(atPath: url.path) {
                try? FileManager.default.removeItem(at: url)
            }
            return
        }

        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        guard let data = try? encoder.encode(tags) else { return }
        try? data.write(to: url)
    }
}
