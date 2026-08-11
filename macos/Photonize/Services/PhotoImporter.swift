import Foundation

/// Where imported files land relative to the existing set. `insert` splices the
/// new photos in at a position; `distribute` interleaves them evenly through the
/// sequence (the way you merge two shoots). Mirrors the Windows `ImportMode`.
enum ImportMode: Equatable {
    case insert(at: Int)
    case distribute
}

/// One planned placement: a source URL (an existing photo or a new file) and the
/// final 0-based position it should occupy once the folder is renumbered. Pure
/// data so the position maths can be unit-tested without touching disk.
struct ImportOperation: Equatable {
    let source: URL
    let isNew: Bool
    let finalIndex: Int
}

/// Copies dropped image files into a folder and renumbers the whole folder to
/// `prefix-NNNNN`, ported from the Windows `PhotoImporter`.
///
/// The important behaviours preserved from the C# original:
/// - Duplicate sources are collapsed and anything already in the target folder
///   is dropped from the batch.
/// - Final positions are computed first (insert splices, distribute interleaves).
/// - New files are **copied, never moved** — a Finder drag that silently moved
///   someone's originals would be a nasty surprise.
/// - The renumber itself is the two-pass `FileRenamer`, so incoming names cannot
///   collide with existing ones and there is exactly one implementation of the
///   naming rule across rename and import.
actor PhotoImporter {
    enum ImportError: LocalizedError {
        case cancelled
        case allInTarget
        case renameFailed(String)

        var errorDescription: String? {
            switch self {
            case .cancelled:            return "Import cancelled."
            case .allInTarget:          return "All selected files are already in this folder."
            case .renameFailed(let m):  return m
            }
        }
    }

    private let renamer = FileRenamer()

    /// Import `sources` into `directory`, placing them per `mode`, and renumber
    /// the folder to `prefix-NNNNN`. Returns the resulting image items in final
    /// order (thumbnails left nil — the caller reloads the folder to fill them).
    ///
    /// `progress` reports the copy phase, which is the slow part for large RAWs.
    /// Cancellation is honoured only up to the point the renumber begins: once a
    /// file has been renamed the folder must reach a consistent end state, so the
    /// short window where cancelling is safe is the copy loop, before any rename.
    func importPhotos(
        _ sources: [URL],
        into directory: URL,
        existing: [PhotoItem],
        prefix: String,
        mode: ImportMode,
        progress: @MainActor (Int, Int, String) -> Void
    ) async throws -> [PhotoItem] {
        let newFiles = Self.sanitizedSources(sources, into: directory)
        guard !newFiles.isEmpty else { throw ImportError.allInTarget }

        let existingURLs = existing
            .filter { !$0.isFolder }
            .sorted { $0.displayOrder < $1.displayOrder }
            .map { $0.filePath }

        let operations = Self.plan(existing: existingURLs, newFiles: newFiles, mode: mode)

        // --- Copy phase: bring the new files in under hidden temp names. ---
        // Cancelling here is safe: nothing existing has been touched yet, so we
        // just delete the temp copies made so far and leave the folder as it was.
        var tempForSource: [URL: URL] = [:]
        var createdTemps: [URL] = []
        let newTotal = newFiles.count
        var copied = 0

        do {
            for op in operations where op.isNew {
                if Task.isCancelled {
                    cleanUp(createdTemps)
                    throw ImportError.cancelled
                }
                let ext = op.source.pathExtension
                let tempName = ".photonize-import-\(UUID().uuidString)" + (ext.isEmpty ? "" : ".\(ext)")
                let tempURL = directory.appendingPathComponent(tempName)
                try FileManager.default.copyItem(at: op.source, to: tempURL)
                tempForSource[op.source] = tempURL
                createdTemps.append(tempURL)

                copied += 1
                let done = copied
                await progress(done, newTotal, "Copying \(op.source.lastPathComponent)")
            }
        } catch let error as ImportError {
            throw error
        } catch {
            cleanUp(createdTemps)
            throw error
        }

        // --- Renumber phase: hand the ordered set to the two-pass renamer. ---
        await progress(newTotal, newTotal, "Finalizing…")

        let ordered = operations
            .sorted { $0.finalIndex < $1.finalIndex }
            .map { op -> PhotoItem in
                let path = op.isNew ? (tempForSource[op.source] ?? op.source) : op.source
                return PhotoItem(
                    filePath: path,
                    fileName: path.lastPathComponent,
                    displayOrder: op.finalIndex,
                    isFolder: false,
                    thumbnail: nil
                )
            }

        let result = await renamer.renameFiles(ordered, prefix: prefix)
        guard result.success else {
            // The renamer failed before consuming the temps; remove whatever it
            // left behind so a retry starts clean.
            cleanUp(createdTemps)
            throw ImportError.renameFailed(result.message)
        }

        // Map each ordered item to where it actually landed on disk.
        let finalByID = Dictionary(uniqueKeysWithValues: result.mapping.map { ($0.id, $0.url) })
        return ordered.map { item in
            let url = finalByID[item.id] ?? item.filePath
            return PhotoItem(
                filePath: url,
                fileName: url.lastPathComponent,
                displayOrder: item.displayOrder,
                isFolder: false,
                thumbnail: nil
            )
        }
    }

    private func cleanUp(_ temps: [URL]) {
        for url in temps where FileManager.default.fileExists(atPath: url.path) {
            try? FileManager.default.removeItem(at: url)
        }
    }

    // MARK: - Pure position maths (nonisolated so slice 12's tests hit them directly)

    /// Deduplicate the drop and drop anything already sitting in the target
    /// directory, preserving the original order of what remains. Case-sensitive
    /// path comparison — macOS volumes can be either, and standardizing the path
    /// is the honest key.
    nonisolated static func sanitizedSources(_ sources: [URL], into directory: URL) -> [URL] {
        let targetPath = directory.standardizedFileURL.path
        var seen = Set<String>()
        var result: [URL] = []
        for url in sources {
            let standardized = url.standardizedFileURL
            let key = standardized.path
            guard !seen.contains(key) else { continue }
            seen.insert(key)
            if standardized.deletingLastPathComponent().path == targetPath { continue }
            result.append(url)
        }
        return result
    }

    /// Dispatch to the mode-specific layout.
    nonisolated static func plan(existing: [URL], newFiles: [URL], mode: ImportMode) -> [ImportOperation] {
        switch mode {
        case .insert(let index): return insertPositions(existing: existing, newFiles: newFiles, insertIndex: index)
        case .distribute:        return distributePositions(existing: existing, newFiles: newFiles)
        }
    }

    /// New files land contiguously at `insertIndex`; an index at or past the end
    /// is an append, an index of 0 a prepend. Existing photos before the point
    /// keep their positions and those after it shift down by the new count.
    nonisolated static func insertPositions(existing: [URL], newFiles: [URL], insertIndex: Int) -> [ImportOperation] {
        var operations: [ImportOperation] = []
        let clamped = min(max(insertIndex, 0), existing.count)

        for i in 0..<clamped {
            operations.append(ImportOperation(source: existing[i], isNew: false, finalIndex: i))
        }
        for i in 0..<newFiles.count {
            operations.append(ImportOperation(source: newFiles[i], isNew: true, finalIndex: clamped + i))
        }
        for i in clamped..<existing.count {
            operations.append(ImportOperation(source: existing[i], isNew: false, finalIndex: i + newFiles.count))
        }
        return operations
    }

    /// Interleave the new files evenly through the existing sequence. Each new
    /// file `i` targets `(i+1) * total / (newCount+1)`; the gaps fill with
    /// existing photos in order, so relative order within each group survives.
    nonisolated static func distributePositions(existing: [URL], newFiles: [URL]) -> [ImportOperation] {
        var operations: [ImportOperation] = []
        let oldCount = existing.count
        let newCount = newFiles.count
        let total = oldCount + newCount
        guard total > 0 else { return [] }

        var insertPositions: [Int] = []
        for i in 0..<newCount {
            insertPositions.append((i + 1) * total / (newCount + 1))
        }

        var oldIdx = 0
        var newIdx = 0
        for finalIdx in 0..<total {
            if newIdx < insertPositions.count, finalIdx == insertPositions[newIdx], newIdx < newFiles.count {
                operations.append(ImportOperation(source: newFiles[newIdx], isNew: true, finalIndex: finalIdx))
                newIdx += 1
            } else if oldIdx < existing.count {
                operations.append(ImportOperation(source: existing[oldIdx], isNew: false, finalIndex: finalIdx))
                oldIdx += 1
            } else if newIdx < newFiles.count {
                // Two new files claimed the same slot (dense drops); place the
                // spillover here rather than dropping it.
                operations.append(ImportOperation(source: newFiles[newIdx], isNew: true, finalIndex: finalIdx))
                newIdx += 1
            }
        }
        return operations
    }
}
