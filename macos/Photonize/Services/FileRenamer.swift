import Foundation

struct RenameResult {
    let success: Bool
    let message: String
}

class FileRenamer {
    func validatePrefix(_ prefix: String) -> Bool {
        guard !prefix.trimmingCharacters(in: .whitespaces).isEmpty else {
            return false
        }

        // Check for invalid filename characters
        let invalidChars = CharacterSet(charactersIn: ":/\\")
        return prefix.rangeOfCharacter(from: invalidChars) == nil
    }

    func renameFiles(_ photos: [PhotoItem], prefix: String) async -> RenameResult {
        guard validatePrefix(prefix) else {
            return RenameResult(success: false, message: "Invalid prefix. Please use only valid filename characters.")
        }

        guard !photos.isEmpty else {
            return RenameResult(success: false, message: "No photos to rename.")
        }

        // Sort by display order
        let sortedPhotos = photos.sorted { $0.displayOrder < $1.displayOrder }

        // Prepare rename operations
        var renameOperations: [(oldPath: URL, newPath: URL, item: PhotoItem)] = []

        for (index, photo) in sortedPhotos.enumerated() {
            let fileExtension = photo.filePath.pathExtension
            let directory = photo.filePath.deletingLastPathComponent()

            // Generate new filename: prefix-00001.ext, prefix-00002.ext, etc.
            let newFileName = "\(prefix)-\(String(format: "%05d", index + 1)).\(fileExtension)"
            let newPath = directory.appendingPathComponent(newFileName)

            // Check if the new name would conflict with an existing file
            // that's not in our rename list
            if FileManager.default.fileExists(atPath: newPath.path) &&
               !sortedPhotos.contains(where: { $0.filePath == newPath }) {
                return RenameResult(
                    success: false,
                    message: "File already exists: \(newFileName). Please choose a different prefix."
                )
            }

            renameOperations.append((oldPath: photo.filePath, newPath: newPath, item: photo))
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

            return RenameResult(
                success: true,
                message: "Successfully renamed \(tempOperations.count) file(s)."
            )
        } catch {
            return RenameResult(
                success: false,
                message: "Error during rename: \(error.localizedDescription)"
            )
        }
    }
}
