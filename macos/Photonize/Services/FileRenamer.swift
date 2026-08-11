import Foundation

struct RenameResult {
    let success: Bool
    let message: String
    /// Where each input item ended up on disk, keyed by its id. Callers update
    /// their model from this rather than recomputing names, so there is exactly
    /// one authoritative source for the naming rule (slice 10 relies on this to
    /// carry tags through a rename).
    let mapping: [(id: PhotoItem.ID, url: URL)]
}

class FileRenamer {
    /// The single source of truth for a renamed file's name. Index is 0-based;
    /// the visible number is 1-based, which is the classic place the Windows and
    /// macOS clients could drift.
    func targetName(prefix: String, index: Int, extension ext: String) -> String {
        "\(prefix)-\(String(format: "%05d", index + 1)).\(ext)"
    }

    /// Validate a rename prefix, returning `nil` when it is acceptable or a
    /// human-readable reason when it is not. Mirrors the Windows rejection set
    /// (`Path.GetInvalidFileNameChars()` plus the reserved dot names) so the two
    /// clients agree on what a legal prefix is.
    func validatePrefix(_ prefix: String) -> String? {
        if prefix.isEmpty {
            return "Please enter a prefix for the files."
        }

        if prefix != prefix.trimmingCharacters(in: .whitespaces) {
            return "The prefix cannot start or end with a space."
        }

        if prefix == "." || prefix == ".." {
            return "\"\(prefix)\" is a reserved name and cannot be used as a prefix."
        }

        if prefix.hasPrefix(".") {
            return "The prefix cannot start with a dot — the renamed files would be hidden."
        }

        // POSIX-illegal "/" and NUL, plus ":" (Finder still treats it specially)
        // and "\" (kept for cross-platform sanity).
        let invalidChars = CharacterSet(charactersIn: "/:\\").union(CharacterSet(charactersIn: "\0"))
        if prefix.rangeOfCharacter(from: invalidChars) != nil {
            return "The prefix cannot contain the characters / : \\ or a null byte."
        }

        return nil
    }

    func renameFiles(_ photos: [PhotoItem], prefix: String) async -> RenameResult {
        if let reason = validatePrefix(prefix) {
            return RenameResult(success: false, message: reason, mapping: [])
        }

        guard !photos.isEmpty else {
            return RenameResult(success: false, message: "No photos to rename.", mapping: [])
        }

        // Sort by display order
        let sortedPhotos = photos.sorted { $0.displayOrder < $1.displayOrder }

        // Prepare rename operations
        var renameOperations: [(oldPath: URL, newPath: URL, id: PhotoItem.ID)] = []

        for (index, photo) in sortedPhotos.enumerated() {
            let fileExtension = photo.filePath.pathExtension
            let directory = photo.filePath.deletingLastPathComponent()

            // Generate new filename: prefix-00001.ext, prefix-00002.ext, etc.
            let newFileName = targetName(prefix: prefix, index: index, extension: fileExtension)
            let newPath = directory.appendingPathComponent(newFileName)

            // Check if the new name would conflict with an existing file
            // that's not in our rename list
            if FileManager.default.fileExists(atPath: newPath.path) &&
               !sortedPhotos.contains(where: { $0.filePath == newPath }) {
                return RenameResult(
                    success: false,
                    message: "File already exists: \(newFileName). Please choose a different prefix.",
                    mapping: []
                )
            }

            renameOperations.append((oldPath: photo.filePath, newPath: newPath, id: photo.id))
        }

        do {
            // Use temporary names to avoid conflicts during rename
            var tempOperations: [(tempPath: URL, finalPath: URL)] = []

            // First pass: rename to temporary names
            for operation in renameOperations {
                if operation.oldPath == operation.newPath {
                    continue // Already has the correct name
                }

                let tempPath = operation.oldPath.appendingPathExtension("tmp_rename")
                try FileManager.default.moveItem(at: operation.oldPath, to: tempPath)
                tempOperations.append((tempPath: tempPath, finalPath: operation.newPath))
            }

            // Second pass: rename from temporary to final names
            for operation in tempOperations {
                try FileManager.default.moveItem(at: operation.tempPath, to: operation.finalPath)
            }

            // Every item ends at its computed target (unchanged items included), so
            // the mapping reflects exactly what is now on disk.
            let mapping = renameOperations.map { (id: $0.id, url: $0.newPath) }
            return RenameResult(
                success: true,
                message: "Successfully renamed \(tempOperations.count) file(s).",
                mapping: mapping
            )
        } catch {
            return RenameResult(
                success: false,
                message: "Error during rename: \(error.localizedDescription)",
                mapping: []
            )
        }
    }
}
