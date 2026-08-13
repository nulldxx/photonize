import Foundation
import SwiftUI
import UniformTypeIdentifiers

@MainActor
class PhotoGridViewModel: ObservableObject {
    @Published var photos: [PhotoItem] = []
    @Published var directoryPath: String = "" { didSet { scheduleSave() } }
    @Published var renamePrefix: String = "" { didSet { scheduleSave() } }
    @Published var thumbnailSize: Double = 200 { didSet { scheduleSave(); scheduleThumbnailResize() } }
    @Published var statusMessage: String = "Ready"
    @Published var isLoading: Bool = false
    @Published var selectedPhotos: Set<PhotoItem.ID> = []
    @Published var isPreviewVisible: Bool = false { didSet { scheduleSave() } }
    @Published var isTextEditorVisible: Bool = false { didSet { scheduleSave() } }
    @Published var restoreLastFolderOnLaunch: Bool = true { didSet { scheduleSave() } }
    @Published var previewPhoto: PhotoItem? { didSet { onPreviewChanged() } }
    /// The prompt/caption text bound to the editor tab. Edits are debounced onto
    /// the `.txt` sidecar of `promptTarget`, never of the live `previewPhoto`, so
    /// switching photos mid-edit can't cross-contaminate (slice 09).
    @Published var promptText: String = "" { didSet { onPromptEdited() } }
    @Published var hasUnsavedChanges: Bool = false
    /// Drives the shared progress sheet. Non-nil while a batch operation (export,
    /// and later import/tag-move) is running; setting it to nil dismisses the sheet.
    @Published var operationProgress: OperationProgress?
    /// Non-nil while the import-mode sheet (insert vs distribute) is asking the
    /// user how to place a Finder drop; the awaiting import resumes on their answer.
    @Published var importModeRequest: ImportModeRequest?
    /// Non-nil while the "move tagged images into sub-folders" sheet is up,
    /// collecting a destination folder name per tag (slice 10).
    @Published var tagFolderRequest: TagFolderRequest?
    /// The focus cursor for keyboard navigation. Published so the grid can scroll
    /// it into view as it moves.
    @Published var focusedPhotoID: PhotoItem.ID?

    /// The fixed point of a range selection: shift-click and shift-arrow extend
    /// from here without moving it.
    private var selectionAnchor: PhotoItem.ID?
    /// Columns currently laid out in the grid, published from the view on resize
    /// so up/down navigation knows how far a row jump is.
    var columnCount: Int = 1

    private let fileRenamer = FileRenamer()
    private let imageExporter = ImageExporter()
    private let thumbnailGenerator = ThumbnailGenerator()
    private let thumbnailLoader = ThumbnailLoader()
    private let settingsManager = SettingsManager()
    private let directoryAccess = DirectoryAccess()
    private let tagService = TagService()

    /// Generation of the current fill pass. Every thumbnail request carries it;
    /// results stamped with a stale generation are dropped. Bumped whenever we
    /// start loading a folder or re-render at a larger size.
    private var loadGeneration = 0
    /// The `maxSize` the visible thumbnails were last decoded at, so a slider
    /// *increase* can re-request sharper bitmaps while a decrease is a no-op.
    private var renderedThumbnailSize = 0

    /// Completed thumbnails waiting to be written back into `photos`. Buffered and
    /// flushed on a timer so a 500-photo folder republishes the array in batches
    /// rather than once per photo.
    private var pendingThumbnails: [(id: PhotoItem.ID, image: NSImage)] = []
    private var flushTask: Task<Void, Never>?
    /// Debounces slider drags so we re-render once the user settles, not on every
    /// intermediate step.
    private var resizeTask: Task<Void, Never>?

    /// The single security-scoped root folder currently being accessed, if any.
    private var scopedURL: URL?
    /// Security-scoped bookmark for `scopedURL`, persisted so the folder can be
    /// reopened on the next launch under the sandbox.
    private var directoryBookmark: Data?

    /// Debounces persistence so continuous changes (e.g. dragging the thumbnail
    /// slider) coalesce into a single write.
    private var saveTask: Task<Void, Never>?
    /// Suppresses `didSet` saves while settings are being applied at launch.
    private var isApplyingSettings = false

    // MARK: - Prompt editor (slice 09)

    private let promptStore = PromptFileStore()
    /// The image `promptText` currently belongs to. The debounced save and the
    /// flush both target *this*, never the live `previewPhoto`, so a selection
    /// change mid-edit lands A's text on A's file (the `f9943e0` bug class).
    private var promptTarget: URL?
    /// True while `loadPrompt` is replacing `promptText`; suppresses the resulting
    /// `didSet` so loading a file never schedules a save against it.
    private var isLoadingPrompt = false
    /// Set when the user edits, cleared once written, so plain navigation between
    /// photos never rewrites unchanged `.txt` files.
    private var promptDirty = false
    private var promptSaveTask: Task<Void, Never>?

    /// Set by `AppDelegate.applicationWillFinishLaunching` from the command line,
    /// before this view model is created. `init` consumes it once so a `-d`/`-e`
    /// launch skips the saved-folder restore and acts on the argument instead of
    /// loading the last folder and then jumping away from it (slice 11).
    static var pendingLaunchIntent: LaunchIntent = .none

    init() {
        let intent = Self.pendingLaunchIntent
        Self.pendingLaunchIntent = .none

        // A launch argument owns the directory, so don't restore the saved folder
        // underneath it — but keep every other preference (size, prefix, panes).
        loadSettings(skipDirectoryRestore: intent.suppressesDirectoryRestore)

        if intent != .none {
            Task { await applyLaunchIntent(intent) }
        }
    }

    // MARK: - Launch / open integration (slice 11)

    /// Act on a launch- or open-time intent: navigate to a folder, navigate to a
    /// folder and pre-select some of its images, or export images straight to
    /// WebP. Routed here from the command line, `application(_:open:)`, the Dock,
    /// "Open With", and the Finder Services.
    func applyLaunchIntent(_ intent: LaunchIntent) async {
        switch intent {
        case .none:
            break
        case .openDirectory(let url):
            await openDirectory(url)
        case .openImages(let urls):
            guard let first = urls.first else { return }
            await openDirectory(first.deletingLastPathComponent(), selecting: urls)
        case .exportImages(let urls):
            await exportFilesToWebP(urls)
        }
    }

    /// Adopt `url` as the security-scoped root, load it, and optionally pre-select
    /// some of its files. The single entry point for `open`, the Dock, "Open
    /// With", and the Finder "Open in Photonize" service. URLs arriving from those
    /// paths carry a transient Powerbox grant, so we bookmark immediately and
    /// persist, keeping the folder reachable on the next launch.
    func openDirectory(_ url: URL, selecting selection: [URL] = []) async {
        setScopedRoot(url)
        directoryPath = url.path
        await loadPhotos()
        if !selection.isEmpty {
            selectFiles(matching: selection)
        }
    }

    /// Relinquish the previous security-scoped root, adopt `url`, and bookmark it
    /// for relaunch. Shared by the Browse… panel and every launch/open path.
    private func setScopedRoot(_ url: URL) {
        if let scopedURL {
            directoryAccess.relinquish(scopedURL)
        }
        // Start security-scoped access before we enumerate or bookmark. An
        // NSOpenPanel URL carries an implicit, already-active grant, but a folder
        // arriving from "Open With", the Dock, `open`, or a Finder Service does
        // not — without this call the sandbox denies the directory listing (the
        // user only granted access to the item they clicked, and we must consume
        // that grant here). The matching stop happens in `relinquish` when the
        // root is next replaced. The return is ignored on purpose: a non-sandboxed
        // debug build has access anyway, and a false result must not block the load.
        _ = url.startAccessingSecurityScopedResource()
        scopedURL = url
        directoryBookmark = directoryAccess.makeBookmark(for: url)
    }

    /// Select the grid items whose files match `urls` (by standardized path),
    /// focus the first and preview it — reflecting a Finder "open these images"
    /// selection once the containing folder has loaded.
    private func selectFiles(matching urls: [URL]) {
        let wanted = Set(urls.map { $0.standardizedFileURL.path })
        let matched = photos.filter { !$0.isFolder && wanted.contains($0.filePath.standardizedFileURL.path) }
        guard let firstID = matched.first?.id else { return }

        selectedPhotos = Set(matched.map { $0.id })
        selectionAnchor = firstID
        focusedPhotoID = firstID
        updatePreview(touchedID: firstID)
    }

    /// Export image files straight to WebP — the `-e` command-line verb and the
    /// Finder "Export to WebP" service. Unlike Windows (one app instance per file),
    /// this single instance handles the whole selection. Opens the containing
    /// folder as the scoped root, runs the batch behind the progress sheet, then
    /// reveals the WebP output folder (via `finishExport`).
    func exportFilesToWebP(_ urls: [URL]) async {
        let images = urls.filter {
            thumbnailGenerator.isImageFile($0) && FileManager.default.fileExists(atPath: $0.path)
        }
        guard let first = images.first else {
            showAlert(title: "No Images", message: "No supported image files found in the selection.")
            return
        }

        let baseDirectory = first.deletingLastPathComponent()
        // Adopt the containing folder so the WebP subfolder can be written and the
        // post-export navigation lands somewhere we can read.
        setScopedRoot(baseDirectory)

        let sources = images.map {
            PhotoItem(filePath: $0, fileName: $0.lastPathComponent, displayOrder: 0, isFolder: false, thumbnail: nil)
        }

        let progress = OperationProgress(
            title: "Exporting to \(ExportFormat.webp.folderName)",
            status: "Preparing…",
            total: sources.count
        )
        let exporter = imageExporter

        let task = Task { @MainActor [weak self] in
            guard let self else { return }
            do {
                let result = try await exporter.export(
                    sources,
                    in: baseDirectory,
                    as: .webp,
                    overwrite: { [weak self] existing in
                        self?.promptOverwrite(existing, format: .webp) ?? .cancel
                    },
                    progress: { [weak self] current, total, status in
                        self?.operationProgress?.update(current: current, total: total, status: status)
                    }
                )
                self.operationProgress = nil
                self.finishExport(result)
            } catch is CancellationError {
                self.operationProgress = nil
                self.statusMessage = "Export cancelled."
            } catch ExportError.cancelled {
                self.operationProgress = nil
                self.statusMessage = "Export cancelled."
            } catch {
                self.operationProgress = nil
                self.statusMessage = "Export failed: \(error.localizedDescription)"
                self.showAlert(title: "Export Failed", message: error.localizedDescription)
            }
        }

        progress.task = task
        operationProgress = progress
    }

    func browseDirectory() {
        let panel = NSOpenPanel()
        panel.canChooseFiles = false
        panel.canChooseDirectories = true
        panel.allowsMultipleSelection = false

        if panel.runModal() == .OK, let url = panel.url {
            setRootDirectory(url)
        }
    }

    /// Adopt a folder the user picked as the new security-scoped root: relinquish
    /// the previous root, bookmark this one for relaunch, and load it.
    private func setRootDirectory(_ url: URL) {
        Task {
            await openDirectory(url)
        }
    }

    /// Load whatever the user typed or pasted into the path field (Enter commits,
    /// matching Windows). Invalid or inaccessible paths set `statusMessage` and
    /// leave the grid untouched rather than throwing an alert.
    func submitDirectoryPath() {
        navigateToPath(directoryPath)
    }

    /// Navigate to `path` (also used by the breadcrumb). Expands a leading `~`,
    /// checks the folder exists and is readable, then loads it. Reuses the
    /// current security scope — a successful read means the folder is inside the
    /// scoped root (or this is a non-sandboxed build); a folder we hold no
    /// bookmark for is unreadable under the sandbox and gets a Browse… nudge.
    func navigateToPath(_ path: String) {
        let trimmed = path.trimmingCharacters(in: .whitespaces)
        guard !trimmed.isEmpty else { return }

        let expanded = (trimmed as NSString).expandingTildeInPath

        var isDir: ObjCBool = false
        guard FileManager.default.fileExists(atPath: expanded, isDirectory: &isDir) else {
            statusMessage = "No such folder: \(trimmed)"
            return
        }
        guard isDir.boolValue else {
            statusMessage = "Not a folder: \(trimmed)"
            return
        }

        // Probe access: NSOpenPanel (Browse…) is the only way to grant the
        // sandbox access to a brand-new folder, so point the user there.
        guard (try? FileManager.default.contentsOfDirectory(atPath: expanded)) != nil else {
            statusMessage = "Can't open \(trimmed) — use Browse… to grant access."
            return
        }

        directoryPath = expanded
        Task {
            await loadPhotos()
        }
    }

    func loadPhotos() async {
        guard !directoryPath.isEmpty else {
            statusMessage = "Invalid directory path"
            return
        }

        let dirURL = URL(fileURLWithPath: directoryPath)
        guard FileManager.default.fileExists(atPath: directoryPath) else {
            statusMessage = "Directory does not exist"
            return
        }

        selectedPhotos.removeAll()
        selectionAnchor = nil
        focusedPhotoID = nil
        previewPhoto = nil
        pendingThumbnails.removeAll()
        flushTask?.cancel()
        flushTask = nil
        statusMessage = "Loading photos..."

        let size = Int(thumbnailSize)
        var displayOrder = 0
        var newPhotos: [PhotoItem] = []

        do {
            // --- Phase 1: enumerate and publish placeholders immediately. ---
            // Folders get their icon up front (cheap); images start with a nil
            // thumbnail so the grid renders at once and fills in during phase 2.

            // Add parent directory navigation
            if let parentURL = dirURL.deletingLastPathComponent().path != "/" ? dirURL.deletingLastPathComponent() : nil {
                let parentItem = PhotoItem(
                    filePath: parentURL,
                    fileName: "..",
                    displayOrder: displayOrder,
                    isFolder: true,
                    thumbnail: thumbnailGenerator.folderIcon(for: parentURL, size: size)
                )
                newPhotos.append(parentItem)
                displayOrder += 1
            }

            let contents = try FileManager.default.contentsOfDirectory(
                at: dirURL,
                includingPropertiesForKeys: [.isDirectoryKey],
                options: [.skipsHiddenFiles]
            )

            let folders = contents.filter { url in
                (try? url.resourceValues(forKeys: [.isDirectoryKey]))?.isDirectory == true
            }.sorted { $0.lastPathComponent < $1.lastPathComponent }

            for folder in folders {
                let folderItem = PhotoItem(
                    filePath: folder,
                    fileName: folder.lastPathComponent,
                    displayOrder: displayOrder,
                    isFolder: true,
                    thumbnail: thumbnailGenerator.folderIcon(for: folder, size: size)
                )
                newPhotos.append(folderItem)
                displayOrder += 1
            }

            // Videos enumerate alongside images so a mixed folder shows and
            // renumbers both, in one filename-sorted sequence.
            let mediaFiles = contents.filter { url in
                guard (try? url.resourceValues(forKeys: [.isDirectoryKey]))?.isDirectory == false else {
                    return false
                }
                return thumbnailGenerator.isMediaFile(url)
            }.sorted { $0.lastPathComponent < $1.lastPathComponent }

            for mediaFile in mediaFiles {
                let photoItem = PhotoItem(
                    filePath: mediaFile,
                    fileName: mediaFile.lastPathComponent,
                    displayOrder: displayOrder,
                    isFolder: false,
                    thumbnail: nil,
                    isVideo: thumbnailGenerator.isVideoFile(mediaFile)
                )
                newPhotos.append(photoItem)
                displayOrder += 1
            }

            // Restore quick-tags saved for this directory (slice 10). Keyed by
            // filename, so a copied or moved folder keeps its badges. Matched
            // case-insensitively via a lowercased index, mirroring the Windows
            // OrdinalIgnoreCase lookup, without rewriting the on-disk key case.
            let savedTags = tagService.load(from: dirURL)
            if !savedTags.isEmpty {
                let index = Dictionary(
                    savedTags.map { ($0.key.lowercased(), $0.value) },
                    uniquingKeysWith: { first, _ in first }
                )
                for i in newPhotos.indices where !newPhotos[i].isFolder {
                    if let tag = index[newPhotos[i].fileName.lowercased()] {
                        newPhotos[i].tag = tag
                    }
                }
            }

            photos = newPhotos
            detectCommonPrefix()

            let photoCount = photos.filter { !$0.isFolder }.count
            let folderCount = photos.filter { $0.isFolder && $0.fileName != ".." }.count
            statusMessage = "Loaded \(folderCount) folder(s) and \(photoCount) photo(s)"

            hasUnsavedChanges = false
        } catch {
            statusMessage = "Error loading photos: \(error.localizedDescription)"
            isLoading = false
            return
        }

        // --- Phase 2: decode thumbnails through the bounded loader. ---
        await fillThumbnails(size: size)
    }

    /// Decode a thumbnail for every image photo currently in `photos`, writing
    /// each result back by `id` as it arrives. Shared by the initial load and by
    /// a slider increase; a fresh generation supersedes any pass still running.
    private func fillThumbnails(size: Int) async {
        let gen = await thumbnailLoader.cancelAll()
        loadGeneration = gen
        renderedThumbnailSize = size

        let jobs = photos.filter { !$0.isFolder }.map { ($0.id, $0.filePath) }
        guard !jobs.isEmpty else {
            isLoading = false
            return
        }

        isLoading = true
        await withTaskGroup(of: Void.self) { group in
            for (id, url) in jobs {
                group.addTask { [weak self] in
                    guard let self else { return }
                    if let image = await self.thumbnailLoader.request(url, size: size, generation: gen) {
                        await self.enqueueThumbnail(id: id, image: image, generation: gen)
                    }
                }
            }
        }

        // Only the pass that is still current gets to declare the load finished;
        // a superseded pass leaves the newer one's state untouched.
        guard gen == loadGeneration else { return }
        flushPendingThumbnails()
        isLoading = false
    }

    /// Buffer a completed thumbnail and arm the coalescing flush timer. Stale
    /// generations are dropped here as a second line of defence (the loader
    /// already drops them).
    private func enqueueThumbnail(id: PhotoItem.ID, image: NSImage, generation gen: Int) {
        guard gen == loadGeneration else { return }
        pendingThumbnails.append((id, image))

        guard flushTask == nil else { return }
        flushTask = Task { [weak self] in
            try? await Task.sleep(nanoseconds: 50_000_000)
            guard !Task.isCancelled else { return }
            self?.flushPendingThumbnails()
        }
    }

    /// Write every buffered thumbnail into `photos` in one go — write-back is by
    /// `id`, not index, because reordering may have moved items since the request
    /// was enqueued.
    private func flushPendingThumbnails() {
        flushTask?.cancel()
        flushTask = nil
        guard !pendingThumbnails.isEmpty else { return }

        let batch = pendingThumbnails
        pendingThumbnails.removeAll(keepingCapacity: true)
        for (id, image) in batch {
            if let index = photos.firstIndex(where: { $0.id == id }) {
                photos[index].thumbnail = image
            }
        }
    }

    /// Re-render thumbnails when the slider settles on a *larger* size. Shrinking
    /// keeps the existing (sharper-than-needed) bitmaps, so it is a no-op beyond
    /// recording the new size.
    private func scheduleThumbnailResize() {
        guard !isApplyingSettings, !photos.isEmpty else { return }

        resizeTask?.cancel()
        resizeTask = Task { [weak self] in
            try? await Task.sleep(nanoseconds: 300_000_000)
            guard !Task.isCancelled, let self else { return }

            let newSize = Int(self.thumbnailSize)
            if newSize > self.renderedThumbnailSize {
                await self.fillThumbnails(size: newSize)
            } else {
                self.renderedThumbnailSize = newSize
            }
        }
    }

    func applyRename() async {
        let photoItems = photos.filter { !$0.isFolder }

        guard !photoItems.isEmpty else {
            showAlert(title: "Warning", message: "No photos to rename.")
            return
        }

        // Surface the specific reason (reserved name, hidden-dot, illegal char)
        // rather than a generic message, and bail before the confirmation prompt.
        if let reason = fileRenamer.validatePrefix(renamePrefix) {
            showAlert(title: "Invalid Prefix", message: reason)
            return
        }

        // Only show confirmation if prefix is changing
        if !filesAlreadyUsePrefix() {
            let continueRename = await showConfirmation(
                title: "Confirm Rename",
                message: "This will rename \(photoItems.count) file(s) using the pattern:\n\(renamePrefix)-00001, \(renamePrefix)-00002, etc.\n\nDo you want to continue?"
            )

            if !continueRename {
                return
            }
        }

        // Land any pending caption edit on the pre-rename .txt so the rename can
        // carry it across to the new name (FileRenamer renames the sidecar too).
        flushPromptSave()

        isLoading = true
        statusMessage = "Renaming files..."

        let result = await fileRenamer.renameFiles(photoItems, prefix: renamePrefix)

        statusMessage = result.message
        isLoading = false

        if result.success {
            // Update the model from what actually landed on disk, not a second
            // computation of the naming rule.
            for entry in result.mapping {
                if let photoIndex = photos.firstIndex(where: { $0.id == entry.id }) {
                    photos[photoIndex].filePath = entry.url
                    photos[photoIndex].fileName = entry.url.lastPathComponent
                }
            }

            // Tags travel with their items through a rename: each item already
            // carries its tag (only its URL changed), so re-saving the sidecar
            // rewrites it under the new filenames with no stale entries (slice 10).
            tagService.save(photos, to: URL(fileURLWithPath: directoryPath))

            // Repoint the inspector at the renamed file so the editor keeps
            // showing the (now relocated) caption rather than a stale path.
            if let pid = previewPhoto?.id, let updated = photos.first(where: { $0.id == pid }) {
                previewPhoto = updated
            }

            hasUnsavedChanges = false
        } else {
            showAlert(title: "Error", message: result.message)
        }
    }

    /// How the batch handles a volume where the Trash is unavailable. Decided
    /// lazily, the first time `trashItem` actually fails, then applied to the
    /// rest of the batch.
    private enum TrashFallback { case undecided, permanent, declined }

    func deleteSelectedPhotos() async {
        // Folders are deletable (recursively, via the Trash); the ".." tile never is.
        let itemsToDelete = photos.filter { selectedPhotos.contains($0.id) && $0.kind != .parent }

        guard !itemsToDelete.isEmpty else { return }

        let folders = itemsToDelete.filter { $0.isFolder }
        let files = itemsToDelete.filter { !$0.isFolder }

        // Per-kind confirmation wording, mirroring Windows.
        let message: String
        if itemsToDelete.count == 1 {
            let item = itemsToDelete[0]
            message = item.isFolder
                ? "Move this folder and all its contents to the Trash?\n\n\(item.fileName)"
                : "Move this file to the Trash?\n\n\(item.fileName)"
        } else {
            var parts: [String] = []
            if !files.isEmpty { parts.append("\(files.count) file(s)") }
            if !folders.isEmpty { parts.append("\(folders.count) folder(s) and all their contents") }
            message = "Move \(parts.joined(separator: " and ")) to the Trash?"
        }

        let confirmed = await showConfirmation(title: "Move to Trash", message: message)
        guard confirmed else { return }

        var deletedNames: [String] = []
        var failedItems: [String] = []
        var fallback: TrashFallback = .undecided

        for item in itemsToDelete {
            // The user was asked once and said no to permanent deletion: stop.
            if fallback == .declined { break }

            do {
                if fallback == .permanent {
                    try FileManager.default.removeItem(at: item.filePath)
                } else {
                    do {
                        try FileManager.default.trashItem(at: item.filePath, resultingItemURL: nil)
                    } catch {
                        // The honest warning, at the moment it actually matters: a
                        // volume with no Trash (e.g. an SMB share). Ask once and
                        // apply the answer to the whole batch.
                        let permit = await showConfirmation(
                            title: "Can't Use the Trash Here",
                            message: "The Trash isn't available on this volume (for example a network share).\n\nDelete permanently instead? This cannot be undone."
                        )
                        guard permit else {
                            fallback = .declined
                            break
                        }
                        fallback = .permanent
                        try FileManager.default.removeItem(at: item.filePath)
                    }
                }

                if let index = photos.firstIndex(where: { $0.id == item.id }) {
                    photos.remove(at: index)
                }
                deletedNames.append(item.fileName)
            } catch {
                failedItems.append("\(item.fileName): \(error.localizedDescription)")
            }
        }

        // Deleting renumbers but must not arm the quit guard (matches Windows).
        updateDisplayOrder(updateUnsavedChanges: false)

        let trashed = fallback != .permanent
        if deletedNames.count == 1 {
            statusMessage = trashed ? "Moved \(deletedNames[0]) to Trash" : "Deleted \(deletedNames[0])"
        } else if !deletedNames.isEmpty {
            statusMessage = trashed ? "Moved \(deletedNames.count) item(s) to Trash" : "Deleted \(deletedNames.count) item(s)"
        }

        if !failedItems.isEmpty {
            showAlert(
                title: "Delete Errors",
                message: "Failed to delete \(failedItems.count) item(s):\n\n\(failedItems.joined(separator: "\n"))"
            )
        }

        selectedPhotos.removeAll()
        selectionAnchor = nil
        focusedPhotoID = nil
    }

    // MARK: - Export

    /// Right-click → Export ▸ WebP/PNG/JPG. Resolves the source set, then runs the
    /// batch behind the shared progress sheet and navigates into the output folder
    /// on success. Kicks off a `Task` so the menu action returns immediately.
    func exportSelection(_ contextPhoto: PhotoItem, as format: ExportFormat) {
        // A folder exports the images it directly contains; the exporter writes the
        // subfolder inside it. An image exports the selection when it is part of
        // one, otherwise itself alone (the Windows right-click rule).
        let sources: [PhotoItem]
        let baseDirectory: URL
        if contextPhoto.isFolder {
            sources = imageFiles(in: contextPhoto.filePath)
            baseDirectory = contextPhoto.filePath
        } else {
            baseDirectory = URL(fileURLWithPath: directoryPath)
            if selectedPhotos.contains(contextPhoto.id) {
                sources = photos.filter { selectedPhotos.contains($0.id) && !$0.isFolder }
            } else {
                sources = [contextPhoto]
            }
        }

        // The image exporter can't read a video, so drop videos from a mixed
        // selection and report them as skipped rather than failing each clip.
        let (imageSources, skippedVideos) = Self.partitionExportSources(sources)

        guard !imageSources.isEmpty else {
            statusMessage = skippedVideos > 0
                ? "Nothing to export — \(skippedVideos) video(s) can't be exported."
                : "No images to export."
            return
        }

        let progress = OperationProgress(
            title: "Exporting to \(format.folderName)",
            status: "Preparing…",
            total: imageSources.count
        )
        let exporter = imageExporter

        let task = Task { @MainActor [weak self] in
            guard let self else { return }
            do {
                let result = try await exporter.export(
                    imageSources,
                    in: baseDirectory,
                    as: format,
                    overwrite: { [weak self] existing in
                        self?.promptOverwrite(existing, format: format) ?? .cancel
                    },
                    progress: { [weak self] current, total, status in
                        self?.operationProgress?.update(current: current, total: total, status: status)
                    }
                )
                self.operationProgress = nil
                self.finishExport(result, skippedVideos: skippedVideos)
            } catch is CancellationError {
                self.operationProgress = nil
                self.statusMessage = "Export cancelled."
            } catch ExportError.cancelled {
                self.operationProgress = nil
                self.statusMessage = "Export cancelled."
            } catch {
                self.operationProgress = nil
                self.statusMessage = "Export failed: \(error.localizedDescription)"
                self.showAlert(title: "Export Failed", message: error.localizedDescription)
            }
        }

        progress.task = task
        operationProgress = progress
    }

    /// Split export sources into the images the exporter can handle and the count
    /// of videos to report as skipped. Static and pure so slice 12 can assert the
    /// mixed-selection rule without driving the whole export.
    nonisolated static func partitionExportSources(_ sources: [PhotoItem]) -> (images: [PhotoItem], skippedVideos: Int) {
        let images = sources.filter { !$0.isVideo }
        return (images, sources.count - images.count)
    }

    /// The image files directly inside `folderURL`, sorted by name — the folder
    /// equivalent of the grid's own image list.
    private func imageFiles(in folderURL: URL) -> [PhotoItem] {
        guard let contents = try? FileManager.default.contentsOfDirectory(
            at: folderURL,
            includingPropertiesForKeys: [.isDirectoryKey],
            options: [.skipsHiddenFiles]
        ) else { return [] }

        return contents
            .filter { url in
                (try? url.resourceValues(forKeys: [.isDirectoryKey]))?.isDirectory == false
                    && thumbnailGenerator.isImageFile(url)
            }
            .sorted { $0.lastPathComponent < $1.lastPathComponent }
            .map { PhotoItem(filePath: $0, fileName: $0.lastPathComponent, displayOrder: 0, isFolder: false, thumbnail: nil) }
    }

    /// The one overwrite prompt for a batch: Overwrite All / Skip Existing / Cancel,
    /// listing the clashing outputs (capped, as on Windows).
    private func promptOverwrite(_ existing: [String], format: ExportFormat) -> OverwriteOption {
        let alert = NSAlert()
        alert.messageText = "Files Already Exist"

        let listed = existing.prefix(5).joined(separator: "\n")
        let more = existing.count > 5 ? "\n…and \(existing.count - 5) more" : ""
        if existing.count == 1 {
            alert.informativeText = "The file '\(existing[0])' already exists in the \(format.folderName) folder.\n\n"
                + "• Overwrite All\n• Skip Existing\n• Cancel"
        } else {
            alert.informativeText = "\(existing.count) files already exist in the \(format.folderName) folder:\n\n"
                + "\(listed)\(more)\n\n• Overwrite All\n• Skip Existing\n• Cancel"
        }

        alert.addButton(withTitle: "Overwrite All")
        alert.addButton(withTitle: "Skip Existing")
        alert.addButton(withTitle: "Cancel")

        switch alert.runModal() {
        case .alertFirstButtonReturn:  return .overwriteAll
        case .alertSecondButtonReturn: return .skipExisting
        default:                       return .cancel
        }
    }

    /// Report the counts and, when anything was written, navigate into the output
    /// folder to reveal the result (Windows reloads to the same end).
    private func finishExport(_ result: ExportResult, skippedVideos: Int = 0) {
        var parts: [String] = []
        if result.exported > 0 { parts.append("\(result.exported) exported") }
        if result.skipped > 0 { parts.append("\(result.skipped) skipped") }
        if skippedVideos > 0 { parts.append("\(skippedVideos) video(s) skipped") }
        if !result.failed.isEmpty { parts.append("\(result.failed.count) failed") }
        statusMessage = parts.isEmpty ? "Nothing to export." : "Export complete: " + parts.joined(separator: ", ")

        if !result.failed.isEmpty {
            showAlert(
                title: "Export Errors",
                message: "Failed to export \(result.failed.count) item(s):\n\n\(result.failed.joined(separator: "\n"))"
            )
        }

        if result.exported > 0 {
            directoryPath = result.outputURL.path
            Task { await loadPhotos() }
        }
    }

    // MARK: - Import (Finder drop)

    private let photoImporter = PhotoImporter()

    /// Import files dropped from Finder into the current directory, inserting at
    /// `insertIndex` (an image-only position; `distribute` ignores it). Guards
    /// mirror the Windows message boxes: a folder must be open, a prefix set, and
    /// the drop must contain at least one supported image.
    func importPhotos(_ urls: [URL], atImageIndex insertIndex: Int) async {
        guard !directoryPath.isEmpty, FileManager.default.fileExists(atPath: directoryPath) else {
            showAlert(title: "No Folder", message: "Open a folder before importing photos.")
            return
        }
        // A non-empty, legal prefix is required — imported files are renamed into
        // the folder's scheme immediately, so there is nothing to import them as
        // without one.
        if let reason = fileRenamer.validatePrefix(renamePrefix) {
            showAlert(title: "No Prefix", message: reason)
            return
        }

        let supported = urls.filter { thumbnailGenerator.isImageFile($0) }
        guard !supported.isEmpty else {
            showAlert(title: "No Images", message: "No supported image files found in the selection.")
            return
        }

        let directory = URL(fileURLWithPath: directoryPath)
        let newFiles = PhotoImporter.sanitizedSources(supported, into: directory)
        guard !newFiles.isEmpty else {
            showAlert(title: "Already Here", message: "All selected files are already in this folder.")
            return
        }

        let existingImageCount = photos.filter { !$0.isFolder }.count
        let clampedIndex = min(max(insertIndex, 0), existingImageCount)

        guard let mode = await requestImportMode(
            newCount: newFiles.count,
            existingCount: existingImageCount,
            insertIndex: clampedIndex
        ) else {
            return // user cancelled the mode dialog
        }

        let progress = OperationProgress(
            title: "Importing \(newFiles.count) photo(s)",
            status: "Preparing…",
            total: newFiles.count
        )
        let importer = photoImporter
        let snapshot = photos
        let prefix = renamePrefix
        let count = newFiles.count

        let task = Task { @MainActor [weak self] in
            guard let self else { return }
            do {
                _ = try await importer.importPhotos(
                    newFiles,
                    into: directory,
                    existing: snapshot,
                    prefix: prefix,
                    mode: mode,
                    progress: { [weak self] current, total, status in
                        self?.operationProgress?.update(current: current, total: total, status: status)
                    }
                )
                self.operationProgress = nil
                let verb = mode == .distribute ? "distributed" : "inserted"
                self.statusMessage = "Imported \(count) photo(s) — \(verb) into the folder."
                // Reload so the renumbered folder (and its thumbnails, folders and
                // ".." tile) is rebuilt from what actually landed on disk.
                await self.loadPhotos()
            } catch is CancellationError {
                self.operationProgress = nil
                self.statusMessage = "Import cancelled."
                await self.loadPhotos()
            } catch PhotoImporter.ImportError.cancelled {
                self.operationProgress = nil
                self.statusMessage = "Import cancelled."
                await self.loadPhotos()
            } catch {
                self.operationProgress = nil
                self.statusMessage = "Import failed: \(error.localizedDescription)"
                self.showAlert(title: "Import Failed", message: error.localizedDescription)
                await self.loadPhotos()
            }
        }

        progress.task = task
        operationProgress = progress
    }

    /// Present the insert-vs-distribute sheet and await the answer. Returns the
    /// concrete `ImportMode` (carrying the insert index) or nil if cancelled.
    private func requestImportMode(newCount: Int, existingCount: Int, insertIndex: Int) async -> ImportMode? {
        let choice: ImportModeChoice? = await withCheckedContinuation { continuation in
            importModeRequest = ImportModeRequest(
                newCount: newCount,
                existingCount: existingCount,
                insertIndex: insertIndex
            ) { [weak self] choice in
                self?.importModeRequest = nil
                continuation.resume(returning: choice)
            }
        }
        switch choice {
        case .insert:     return .insert(at: insertIndex)
        case .distribute: return .distribute
        case .none:       return nil
        }
    }

    // MARK: - Move into folders

    /// The set of items a drag carries: the whole selection when the dragged item
    /// is part of it (Windows drags multi-selections), otherwise just that item.
    /// The ".." tile is never draggable.
    func dragSet(startingFrom photo: PhotoItem) -> [PhotoItem.ID] {
        guard photo.kind != .parent else { return [] }
        if selectedPhotos.contains(photo.id) {
            return photos.filter { selectedPhotos.contains($0.id) && $0.kind != .parent }.map { $0.id }
        }
        return [photo.id]
    }

    /// Move the given items into `target` (a folder tile or the ".." parent).
    /// Refuses to move a folder into itself or a descendant, and refuses to merge
    /// a folder into an existing folder of the same name — half-merging is worse
    /// than refusing, and Windows refuses too. File collisions prompt once and the
    /// answer governs the batch. Failures are accumulated into one summary rather
    /// than aborting.
    func moveItems(_ ids: [PhotoItem.ID], into target: URL, targetName: String) async {
        let items = ids
            .compactMap { id in photos.first(where: { $0.id == id }) }
            .filter { $0.filePath.standardizedFileURL != target.standardizedFileURL }

        guard !items.isEmpty else { return }
        guard FileManager.default.fileExists(atPath: target.path) else {
            showAlert(title: "Error", message: "Target folder does not exist:\n\(target.path)")
            return
        }

        isLoading = true
        statusMessage = "Moving \(items.count) item(s) to \(targetName)…"

        var movedIDs: [PhotoItem.ID] = []
        var failures: [String] = []
        // filename→digit for tagged files that landed in `target`, so their tags
        // can be moved out of this directory's sidecar and into the target's.
        var movedTags: [String: Int] = [:]
        // Decided lazily on the first file collision, then applied to the rest.
        var fileCollisionPolicy: OverwriteOption?

        move: for item in items {
            let dest = target.appendingPathComponent(item.fileName)
            do {
                if item.isFolder {
                    if Self.isMoveIntoSelfOrDescendant(source: item.filePath, target: target) {
                        failures.append("\(item.fileName): can't move a folder into itself")
                        continue
                    }
                    if FileManager.default.fileExists(atPath: dest.path) {
                        failures.append("\(item.fileName): a folder of that name already exists there "
                            + "(merging folders isn't supported)")
                        continue
                    }
                    try FileManager.default.moveItem(at: item.filePath, to: dest)
                } else {
                    if FileManager.default.fileExists(atPath: dest.path) {
                        if fileCollisionPolicy == nil {
                            fileCollisionPolicy = promptMoveOverwrite(name: item.fileName, folder: targetName)
                        }
                        switch fileCollisionPolicy! {
                        case .cancel:       break move
                        case .skipExisting: continue
                        case .overwriteAll: try FileManager.default.removeItem(at: dest)
                        }
                    }
                    try FileManager.default.moveItem(at: item.filePath, to: dest)
                    // An image's caption travels with it.
                    try? SidecarFiles.moveCompanions(of: item.filePath, toDirectory: target)
                    // A tagged file's badge travels too: note it so the two
                    // sidecars can be reconciled once the batch completes (slice 10).
                    if let tag = item.tag {
                        movedTags[item.fileName] = tag
                    }
                }
                movedIDs.append(item.id)
            } catch {
                failures.append("\(item.fileName): \(error.localizedDescription)")
            }
        }

        photos.removeAll { movedIDs.contains($0.id) }
        selectedPhotos.subtract(movedIDs)
        // Moving out renumbers the remaining photos but, like deleting, must not
        // arm the unsaved-changes quit guard.
        updateDisplayOrder(updateUnsavedChanges: false)

        // Keep the two tag sidecars consistent (slice 10): the moved files' tags
        // leave this directory and join the target's, so navigating into the
        // target still shows their badges.
        if !movedTags.isEmpty {
            var targetTags = tagService.load(from: target)
            for (name, digit) in movedTags { targetTags[name] = digit }
            tagService.write(targetTags, to: target)
            tagService.save(photos, to: URL(fileURLWithPath: directoryPath))
        }

        isLoading = false

        if movedIDs.count == 1 {
            statusMessage = "Moved 1 item to \(targetName)"
        } else if !movedIDs.isEmpty {
            statusMessage = "Moved \(movedIDs.count) item(s) to \(targetName)"
        }

        if !failures.isEmpty {
            showAlert(
                title: "Move Errors",
                message: "Failed to move \(failures.count) item(s):\n\n\(failures.joined(separator: "\n"))"
            )
        }
    }

    /// True when moving `source` into `target` would put a folder inside itself
    /// or one of its own descendants — the one move that would eat the folder.
    nonisolated static func isMoveIntoSelfOrDescendant(source: URL, target: URL) -> Bool {
        let s = source.standardizedFileURL.path
        let t = target.standardizedFileURL.path
        return t == s || t.hasPrefix(s + "/")
    }

    /// The single collision decision for a move batch: Overwrite All / Skip / Cancel.
    private func promptMoveOverwrite(name: String, folder: String) -> OverwriteOption {
        let alert = NSAlert()
        alert.messageText = "File Already Exists"
        alert.informativeText = "'\(name)' already exists in \(folder).\n\n• Overwrite All\n• Skip Existing\n• Cancel"
        alert.addButton(withTitle: "Overwrite All")
        alert.addButton(withTitle: "Skip Existing")
        alert.addButton(withTitle: "Cancel")

        switch alert.runModal() {
        case .alertFirstButtonReturn:  return .overwriteAll
        case .alertSecondButtonReturn: return .skipExisting
        default:                       return .cancel
        }
    }

    // MARK: - Quick tagging (slice 10)

    /// True when any photo in the directory carries a tag. Gates the three
    /// Tags ▸ menu items, matching the Windows `HasTaggedPhotos`.
    var hasTaggedPhotos: Bool {
        photos.contains { !$0.isFolder && $0.tag != nil }
    }

    /// Quick-tag the current selection with `digit` (0-9). Pressing the same digit
    /// again on an already-tagged selection clears it; a different digit replaces.
    /// One tag per photo — never a set. Folders and the ".." tile can't be tagged.
    /// The sidecar is re-saved after every change; it's a tiny file, so debouncing
    /// isn't worth the complexity.
    func applyTag(_ digit: Int) {
        guard (0...9).contains(digit) else { return }

        let targetIDs = photos
            .filter { selectedPhotos.contains($0.id) && !$0.isFolder }
            .map { $0.id }
        guard !targetIDs.isEmpty else {
            statusMessage = "Select one or more photos to tag"
            return
        }

        // Toggle off only when every selected photo already carries this digit.
        let clearing = targetIDs.allSatisfy { id in
            photos.first(where: { $0.id == id })?.tag == digit
        }
        let newTag = clearing ? nil : digit

        for id in targetIDs {
            if let index = photos.firstIndex(where: { $0.id == id }) {
                photos[index].tag = newTag
            }
        }

        tagService.save(photos, to: URL(fileURLWithPath: directoryPath))

        statusMessage = clearing
            ? "Cleared tag from \(targetIDs.count) photo(s)"
            : "Tagged \(targetIDs.count) photo(s) with \(digit)"
    }

    /// Send to ▸ Front / Back. "Front" is the first *media* slot, so a photo can
    /// never land above the ".." tile or a folder; "Back" appends. Selection-aware
    /// via `dragSet(startingFrom:)`: a right-click on an item outside the current
    /// selection acts on that item alone, matching Delete on both platforms.
    /// A genuine reorder, so it arms the unsaved-changes guard.
    func sendTo(_ edge: SendToEdge, photo: PhotoItem) {
        let targets = dragSet(startingFrom: photo)
        guard ListReorder.wouldChangeOrder(photos, moving: targets, to: edge) else { return }

        let moved = ListReorder.sendTo(photos, moving: targets, to: edge)
        let movedCount = photos.filter { targets.contains($0.id) && !$0.isFolder }.count

        withAnimation(.default) {
            photos = moved
        }
        updateDisplayOrder()

        statusMessage = edge == .front
            ? "Sent \(movedCount) photo(s) to the front"
            : "Sent \(movedCount) photo(s) to the back"
    }

    /// Whether a "Send to" from this tile would do anything — drives the menu
    /// entries' disabled state so an already-at-the-edge item is not offered.
    func canSendTo(_ edge: SendToEdge, photo: PhotoItem) -> Bool {
        ListReorder.wouldChangeOrder(photos, moving: dragSet(startingFrom: photo), to: edge)
    }

    /// Tags ▸ Sort by tag. Folders first, then tagged photos by ascending digit,
    /// then untagged (see `TagSorter`). A genuine reorder, so it arms the
    /// unsaved-changes guard and Apply Rename follows naturally.
    func sortByTag() {
        // Clear the selection before replacing the array, so nothing points at a
        // stale ordering (same idiom as loadPhotos).
        selectedPhotos.removeAll()
        selectionAnchor = nil
        focusedPhotoID = nil

        photos = TagSorter.sortByTag(photos)
        updateDisplayOrder()
        statusMessage = "Sorted photos by tag"
    }

    /// Tags ▸ Clear tags. Confirms, then removes every tag in the directory and
    /// deletes the sidecar (`save` with nothing tagged unlinks it).
    func clearTags() async {
        let taggedCount = photos.filter { !$0.isFolder && $0.tag != nil }.count
        guard taggedCount > 0 else { return }

        let confirmed = await showConfirmation(
            title: "Clear Tags",
            message: "Clear tags from \(taggedCount) photo(s)?"
        )
        guard confirmed else { return }

        for i in photos.indices where photos[i].tag != nil {
            photos[i].tag = nil
        }
        tagService.save(photos, to: URL(fileURLWithPath: directoryPath))
        statusMessage = "Cleared tags from \(taggedCount) photo(s)"
    }

    /// Tags ▸ Move tagged images into sub-folders. Presents the per-tag folder
    /// sheet; on confirm, runs the move behind the shared progress sheet.
    func beginMoveTaggedToFolders() {
        let tagged = photos.filter { !$0.isFolder && $0.tag != nil }
        guard !tagged.isEmpty else { return }

        let counts = Dictionary(grouping: tagged, by: { $0.tag! })
            .map { (tag: $0.key, count: $0.value.count) }

        tagFolderRequest = TagFolderRequest(counts: counts) { [weak self] names in
            self?.tagFolderRequest = nil
            guard let self, let names else { return }
            Task { await self.moveTaggedToFolders(names) }
        }
    }

    /// Move each tag's photos into its named sub-folder, carrying the caption
    /// `.txt` (via `SidecarFiles`) and the tag itself (written into the
    /// destination folder's sidecar) so navigating in still shows the badge.
    /// Validates folder names with the same hardened rules as a rename prefix and
    /// refuses a name that clashes with an existing file.
    func moveTaggedToFolders(_ folderNames: [Int: String]) async {
        let tagged = photos.filter { !$0.isFolder && $0.tag != nil }
        guard !tagged.isEmpty else { return }

        let directory = URL(fileURLWithPath: directoryPath)

        // Validate every folder name up front (slice 04's prefix rules also make a
        // sound folder-name check), and refuse a name that is already a *file*
        // here — a sub-folder can't be created over it.
        for (tag, name) in folderNames {
            if let reason = fileRenamer.validatePrefix(name) {
                showAlert(
                    title: "Invalid Folder Name",
                    message: "\"\(name)\" can't be used for tag \(tag): \(reason)"
                )
                return
            }
            let candidate = directory.appendingPathComponent(name)
            var isDir: ObjCBool = false
            if FileManager.default.fileExists(atPath: candidate.path, isDirectory: &isDir), !isDir.boolValue {
                showAlert(
                    title: "Name In Use",
                    message: "\"\(name)\" is already a file in this folder, so it can't be a destination sub-folder."
                )
                return
            }
        }

        // Warn once about files that would be overwritten in a target sub-folder.
        var existing: [String] = []
        for photo in tagged {
            guard let tag = photo.tag, let folder = folderNames[tag] else { continue }
            let dest = directory.appendingPathComponent(folder).appendingPathComponent(photo.fileName)
            if FileManager.default.fileExists(atPath: dest.path) {
                existing.append("\(folder)/\(photo.fileName)")
            }
        }
        var overwrite: OverwriteOption = .overwriteAll
        if !existing.isEmpty {
            overwrite = promptMoveTaggedOverwrite(existing)
            if overwrite == .cancel { return }
        }

        let progress = OperationProgress(
            title: "Moving tagged photos",
            status: "Preparing…",
            total: tagged.count
        )
        let tagService = self.tagService

        let task = Task { @MainActor [weak self] in
            guard let self else { return }

            var movedIDs: Set<PhotoItem.ID> = []
            var moved = 0
            var skipped = 0
            var failures: [String] = []
            // folderName → (filename → digit), written into each destination
            // sidecar so the moved photos keep their badges.
            var destinationTags: [String: [String: Int]] = [:]

            for (i, photo) in tagged.enumerated() {
                if Task.isCancelled { break }
                guard let tag = photo.tag, let folder = folderNames[tag] else { continue }
                self.operationProgress?.update(current: i, total: tagged.count, status: photo.fileName)

                let targetDir = directory.appendingPathComponent(folder)
                let dest = targetDir.appendingPathComponent(photo.fileName)
                do {
                    try FileManager.default.createDirectory(at: targetDir, withIntermediateDirectories: true)

                    if FileManager.default.fileExists(atPath: dest.path) {
                        if overwrite == .skipExisting {
                            skipped += 1
                            continue
                        }
                        try FileManager.default.removeItem(at: dest)
                    }

                    try FileManager.default.moveItem(at: photo.filePath, to: dest)
                    // The caption .txt travels with the image (slice 09 / 06 rule).
                    try? SidecarFiles.moveCompanions(of: photo.filePath, toDirectory: targetDir)

                    destinationTags[folder, default: [:]][photo.fileName] = tag
                    movedIDs.insert(photo.id)
                    moved += 1
                } catch {
                    failures.append("\(photo.fileName): \(error.localizedDescription)")
                }
            }

            // Write each destination folder's sidecar (merging with any tags
            // already there), so navigating into it still shows the badges.
            for (folder, tags) in destinationTags {
                let targetDir = directory.appendingPathComponent(folder)
                var merged = tagService.load(from: targetDir)
                for (name, digit) in tags { merged[name] = digit }
                tagService.write(merged, to: targetDir)
            }

            // Drop the moved entries from this directory's sidecar, keeping the
            // ones that were skipped or failed, then reload so the new sub-folders
            // appear and the moved images are gone (matching Windows).
            let remaining = self.photos.filter { !movedIDs.contains($0.id) }
            tagService.save(remaining, to: directory)

            self.operationProgress = nil
            await self.loadPhotos()

            var parts: [String] = []
            if moved > 0 { parts.append("moved \(moved)") }
            if skipped > 0 { parts.append("skipped \(skipped)") }
            if !failures.isEmpty { parts.append("\(failures.count) failed") }
            self.statusMessage = parts.isEmpty
                ? "No tagged photos moved."
                : "Tagged photos: " + parts.joined(separator: ", ")

            if !failures.isEmpty {
                self.showAlert(
                    title: "Move Errors",
                    message: "Failed to move \(failures.count) file(s):\n\n\(failures.joined(separator: "\n"))"
                )
            }
        }

        progress.task = task
        operationProgress = progress
    }

    /// The single collision decision for a tag-move batch: Overwrite All / Skip /
    /// Cancel, listing the clashing outputs (capped, as elsewhere).
    private func promptMoveTaggedOverwrite(_ existing: [String]) -> OverwriteOption {
        let alert = NSAlert()
        alert.messageText = "Files Already Exist"
        let listed = existing.prefix(10).joined(separator: "\n")
        let more = existing.count > 10 ? "\n…and \(existing.count - 10) more" : ""
        alert.informativeText = "\(existing.count) file(s) already exist in the target sub-folder(s):\n\n"
            + "\(listed)\(more)\n\n• Overwrite All\n• Skip Existing\n• Cancel"
        alert.addButton(withTitle: "Overwrite All")
        alert.addButton(withTitle: "Skip Existing")
        alert.addButton(withTitle: "Cancel")

        switch alert.runModal() {
        case .alertFirstButtonReturn:  return .overwriteAll
        case .alertSecondButtonReturn: return .skipExisting
        default:                       return .cancel
        }
    }

    func openPhoto(_ photo: PhotoItem) {
        if photo.isFolder {
            directoryPath = photo.filePath.path
            Task {
                await loadPhotos()
            }
        } else {
            NSWorkspace.shared.open(photo.filePath)
        }
    }

    func showInFinder(_ photo: PhotoItem) {
        NSWorkspace.shared.activateFileViewerSelecting([photo.filePath])
    }

    func moveItem(from source: IndexSet, to destination: Int) {
        photos.move(fromOffsets: source, toOffset: destination)
        updateDisplayOrder()
    }

    /// Renumber `displayOrder` to match the current array order. Reordering arms
    /// the unsaved-changes guard; deletion passes `updateUnsavedChanges: false`
    /// so trashing a photo never blocks quit, matching the Windows behaviour.
    func updateDisplayOrder(updateUnsavedChanges: Bool = true) {
        for (index, _) in photos.enumerated() {
            photos[index].displayOrder = index
        }
        if updateUnsavedChanges {
            hasUnsavedChanges = !photos.filter({ !$0.isFolder }).isEmpty && !renamePrefix.isEmpty
        }
    }

    // MARK: - Selection & keyboard navigation

    /// Single entry point for keys forwarded by `KeyCatcher`. Returns `true` when
    /// the key was consumed. ⌘A and Delete arrive via the menu bar, not here.
    func handleKey(_ key: GridKey) -> Bool {
        switch key {
        case .arrow(let direction, let shift):
            moveFocus(direction, extending: shift)
            return true
        case .returnKey:
            guard let id = focusedPhotoID, let photo = photos.first(where: { $0.id == id }) else {
                return false
            }
            openPhoto(photo)
            return true
        case .digit(let value):
            // Consume it even when nothing is selected (applyTag then just posts a
            // hint) so the key never beeps through to the system. This only fires
            // when the grid holds focus — a digit typed in the caption editor goes
            // to the text view, never here.
            applyTag(value)
            return true
        }
    }

    /// Handle a click on a photo. Modifier flags are read from the event by the
    /// view; folders are legal click targets (they can be opened/navigated).
    func handleClick(on photo: PhotoItem, commandKey: Bool, shiftKey: Bool) {
        guard let index = photos.firstIndex(where: { $0.id == photo.id }) else { return }

        if commandKey {
            if selectedPhotos.contains(photo.id) {
                selectedPhotos.remove(photo.id)
            } else {
                selectedPhotos.insert(photo.id)
            }
            selectionAnchor = photo.id
            focusedPhotoID = photo.id
        } else if shiftKey,
                  let anchor = selectionAnchor,
                  let anchorIndex = photos.firstIndex(where: { $0.id == anchor }) {
            // Range from the anchor to here; the anchor stays put.
            selectedPhotos = Self.rangeSelection(in: photos, from: anchorIndex, to: index)
            focusedPhotoID = photo.id
        } else {
            selectedPhotos = [photo.id]
            selectionAnchor = photo.id
            focusedPhotoID = photo.id
        }

        updatePreview(touchedID: photo.id)
    }

    /// ⌘A — select every file, folders (and the `..` entry) excluded.
    func selectAllFiles() {
        let ids = Self.fileIDs(in: photos)
        selectedPhotos = Set(ids)
        selectionAnchor = ids.first
        focusedPhotoID = ids.first
        if let firstID = ids.first {
            updatePreview(touchedID: firstID)
        } else {
            previewPhoto = nil
        }
    }

    /// Move the focus cursor one step, wrapping across rows. `extending` grows the
    /// selection from the anchor; otherwise the selection collapses to the cursor.
    func moveFocus(_ direction: FocusDirection, extending: Bool) {
        guard !photos.isEmpty else { return }

        guard let focus = focusedPhotoID,
              let currentIndex = photos.firstIndex(where: { $0.id == focus }) else {
            // No cursor yet: land on the first item.
            let first = photos[0]
            selectedPhotos = [first.id]
            selectionAnchor = first.id
            focusedPhotoID = first.id
            updatePreview(touchedID: first.id)
            return
        }

        guard let nextIndex = Self.nextFocusIndex(
            from: currentIndex,
            direction: direction,
            columnCount: columnCount,
            count: photos.count
        ) else { return }

        let nextID = photos[nextIndex].id

        if extending {
            let anchorID = selectionAnchor ?? focus
            selectionAnchor = anchorID
            if let anchorIndex = photos.firstIndex(where: { $0.id == anchorID }) {
                selectedPhotos = Self.rangeSelection(in: photos, from: anchorIndex, to: nextIndex)
            }
        } else {
            selectedPhotos = [nextID]
            selectionAnchor = nextID
        }

        focusedPhotoID = nextID
        updatePreview(touchedID: nextID)
    }

    /// The preview follows the *last* item touched, not the first in document
    /// order — ⌘-clicking a later photo should show that photo, not jump back.
    private func updatePreview(touchedID: PhotoItem.ID) {
        if selectedPhotos.contains(touchedID) {
            previewPhoto = photos.first(where: { $0.id == touchedID })
        } else if let remaining = photos.first(where: { selectedPhotos.contains($0.id) }) {
            previewPhoto = remaining
        } else {
            previewPhoto = nil
        }
    }

    // MARK: - Prompt editor (slice 09)

    /// The image whose prompt the editor is bound to — the last-touched preview
    /// item, but only when it is an editable image. Folders and videos have no
    /// meaningful prompt file, so the editor shows an unavailable state for them.
    var promptImage: PhotoItem? {
        guard let photo = previewPhoto, photo.kind == .image else { return nil }
        return photo
    }

    /// React to the inspector's target changing: flush the outgoing prompt against
    /// its own file, then load the incoming one. A same-target reassignment
    /// (re-clicking the selected photo, or a thumbnail write-back mutating the
    /// array) is a no-op, so an in-progress edit is never reloaded away.
    private func onPreviewChanged() {
        let newTarget = promptImage?.filePath
        guard newTarget != promptTarget else { return }
        flushPromptSave()
        loadPrompt(for: newTarget)
    }

    /// Replace `promptText` with `target`'s caption (or clear it), without arming
    /// a save. `isLoadingPrompt` guards the `didSet` that the assignment triggers.
    private func loadPrompt(for target: URL?) {
        promptSaveTask?.cancel()
        promptSaveTask = nil
        promptDirty = false
        isLoadingPrompt = true
        defer { isLoadingPrompt = false }

        promptTarget = target
        promptText = target.flatMap { try? promptStore.load(for: $0) } ?? ""
    }

    /// A user keystroke (not a programmatic load): mark dirty and (re)arm the
    /// debounce.
    private func onPromptEdited() {
        guard !isLoadingPrompt else { return }
        promptDirty = true
        schedulePromptSave()
    }

    /// Debounced save, ~500 ms after the last keystroke — writing per character
    /// would be a write storm on a network volume. Captures the target and text
    /// so a save already in flight can't be redirected by a later selection change.
    private func schedulePromptSave() {
        guard let target = promptTarget else { return }
        let text = promptText
        promptSaveTask?.cancel()
        promptSaveTask = Task { [weak self] in
            try? await Task.sleep(nanoseconds: 500_000_000)
            guard !Task.isCancelled, let self else { return }
            try? self.promptStore.save(text, for: target)
            self.promptDirty = false
        }
    }

    /// Write any pending edit immediately, cancelling the debounce. Called on a
    /// selection change (before loading the next prompt), on window resign-key,
    /// and on quit, so nothing typed is ever lost.
    func flushPromptSave() {
        promptSaveTask?.cancel()
        promptSaveTask = nil
        guard promptDirty, let target = promptTarget else { return }
        try? promptStore.save(promptText, for: target)
        promptDirty = false
    }

    // MARK: - Image compare

    /// True when the selection is exactly two non-folder photos — the Windows
    /// `CanCompareImages` rule, gating the context-menu item and View menu entry.
    var canCompare: Bool {
        Self.comparePair(in: photos, selection: selectedPhotos) != nil
    }

    /// The pair to open the compare window with, ordered so A is the earlier
    /// photo. Nil unless exactly two non-folders are selected.
    var comparePair: ComparePair? {
        Self.comparePair(in: photos, selection: selectedPhotos)
    }

    // MARK: - Pure selection helpers (window-free, so slice 12 can unit-test them)

    /// Every non-folder id, in document order.
    nonisolated static func fileIDs(in photos: [PhotoItem]) -> [PhotoItem.ID] {
        photos.filter { !$0.isFolder }.map { $0.id }
    }

    /// The compare pair for a selection: exactly two selected items, both
    /// non-folders, ordered by `displayOrder` so A is the earlier photo (the one
    /// the divider reveals). A folder anywhere in the selection disqualifies it,
    /// as does any count other than two.
    nonisolated static func comparePair(in photos: [PhotoItem], selection: Set<PhotoItem.ID>) -> ComparePair? {
        let selected = photos.filter { selection.contains($0.id) }
        guard selected.count == 2, selected.allSatisfy({ !$0.isFolder }) else { return nil }
        let ordered = selected.sorted { $0.displayOrder < $1.displayOrder }
        return ComparePair(urlA: ordered[0].filePath, urlB: ordered[1].filePath)
    }

    /// The inclusive set of ids between two indices, order-independent.
    nonisolated static func rangeSelection(in photos: [PhotoItem], from anchorIndex: Int, to targetIndex: Int) -> Set<PhotoItem.ID> {
        guard !photos.isEmpty else { return [] }
        let lower = max(0, min(anchorIndex, targetIndex))
        let upper = min(photos.count - 1, max(anchorIndex, targetIndex))
        guard lower <= upper else { return [] }
        return Set(photos[lower...upper].map { $0.id })
    }

    /// Where the focus cursor lands for a direction, or `nil` at a grid edge.
    /// Left/right walk linearly (which is also row wrapping); up/down jump a row.
    nonisolated static func nextFocusIndex(from index: Int, direction: FocusDirection, columnCount: Int, count: Int) -> Int? {
        guard count > 0, index >= 0, index < count else { return nil }
        let columns = max(1, columnCount)
        switch direction {
        case .right: return index + 1 < count ? index + 1 : nil
        case .left:  return index - 1 >= 0 ? index - 1 : nil
        case .down:  let target = index + columns; return target < count ? target : nil
        case .up:    let target = index - columns; return target >= 0 ? target : nil
        }
    }

    private func detectCommonPrefix() {
        let photoItems = photos.filter { !$0.isFolder }

        guard !photoItems.isEmpty else {
            renamePrefix = ""
            return
        }

        let names = photoItems.map { $0.filePath.deletingPathExtension().lastPathComponent }

        if names.count == 1 {
            renamePrefix = trimTrailingNumbersAndSeparators(names[0])
            return
        }

        var commonPrefix = names[0]
        for name in names.dropFirst() {
            commonPrefix = getCommonPrefix(commonPrefix, name)
            if commonPrefix.isEmpty {
                break
            }
        }

        renamePrefix = trimTrailingNumbersAndSeparators(commonPrefix)
    }

    private func filesAlreadyUsePrefix() -> Bool {
        let photoItems = photos.filter { !$0.isFolder }
        guard !photoItems.isEmpty, !renamePrefix.trimmingCharacters(in: .whitespaces).isEmpty else {
            return false
        }

        for photo in photoItems {
            let nameWithoutExtension = photo.filePath.deletingPathExtension().lastPathComponent

            guard nameWithoutExtension.hasPrefix(renamePrefix + "-") else {
                return false
            }

            let suffix = nameWithoutExtension.dropFirst(renamePrefix.count + 1)
            if suffix.isEmpty || !suffix.allSatisfy({ $0.isNumber }) {
                return false
            }
        }

        return true
    }

    private func getCommonPrefix(_ str1: String, _ str2: String) -> String {
        let minLength = min(str1.count, str2.count)
        var commonPrefix = ""

        for (char1, char2) in zip(str1, str2) {
            if char1 == char2 {
                commonPrefix.append(char1)
            } else {
                break
            }
        }

        return commonPrefix
    }

    private func trimTrailingNumbersAndSeparators(_ str: String) -> String {
        var result = str.trimmingCharacters(in: .whitespaces)

        while !result.isEmpty {
            let lastChar = result.last!
            if lastChar.isNumber || lastChar == "-" || lastChar == "_" || lastChar == " " {
                result.removeLast()
            } else {
                break
            }
        }

        return result
    }

    /// Load persisted preferences. `skipDirectoryRestore` (set for a `-d`/`-e`
    /// launch) applies every preference *except* reopening the last folder — the
    /// launch intent owns the directory — while preserving the saved bookmark so
    /// the next plain launch still restores it (slice 11).
    private func loadSettings(skipDirectoryRestore: Bool = false) {
        let settings = settingsManager.loadSettings()

        // Apply persisted preferences without triggering a debounced re-save.
        isApplyingSettings = true
        thumbnailSize = settings.lastThumbnailSize
        if !settings.lastPrefix.isEmpty {
            renamePrefix = settings.lastPrefix
        }
        isPreviewVisible = settings.isPreviewVisible
        isTextEditorVisible = settings.isTextEditorVisible
        restoreLastFolderOnLaunch = settings.restoreLastFolderOnLaunch

        guard !skipDirectoryRestore, settings.restoreLastFolderOnLaunch else {
            // Remember the bookmark so it is not wiped by the next save, but do
            // not reopen the folder.
            directoryBookmark = settings.lastDirectoryBookmark
            isApplyingSettings = false
            return
        }

        // Prefer the security-scoped bookmark: under the sandbox it is the only
        // way to regain access to a folder outside the app container. Fall back
        // to the raw path for non-sandboxed debug builds.
        if let bookmark = settings.lastDirectoryBookmark,
           let resolved = directoryAccess.resolve(bookmark) {
            scopedURL = resolved.url
            directoryBookmark = resolved.refreshedBookmark ?? bookmark
            directoryPath = resolved.url.path
            isApplyingSettings = false
            Task {
                await loadPhotos()
            }
        } else if !settings.lastDirectoryPath.isEmpty
                    && FileManager.default.fileExists(atPath: settings.lastDirectoryPath) {
            directoryPath = settings.lastDirectoryPath
            isApplyingSettings = false
            Task {
                await loadPhotos()
            }
        } else {
            if settings.lastDirectoryBookmark != nil {
                statusMessage = "Last folder is unavailable. Open a folder to begin."
            }
            isApplyingSettings = false
        }
    }

    private func scheduleSave() {
        guard !isApplyingSettings else { return }
        saveTask?.cancel()
        saveTask = Task { [weak self] in
            try? await Task.sleep(nanoseconds: 500_000_000)
            guard !Task.isCancelled else { return }
            self?.saveSettings()
        }
    }

    private func saveSettings() {
        let settings = AppSettings(
            lastDirectoryPath: directoryPath,
            lastThumbnailSize: thumbnailSize,
            lastPrefix: renamePrefix,
            isPreviewVisible: isPreviewVisible,
            isTextEditorVisible: isTextEditorVisible,
            lastDirectoryBookmark: directoryBookmark
        )
        settingsManager.saveSettings(settings)
    }

    private func showAlert(title: String, message: String) {
        let alert = NSAlert()
        alert.messageText = title
        alert.informativeText = message
        alert.alertStyle = .warning
        alert.addButton(withTitle: "OK")
        alert.runModal()
    }

    private func showConfirmation(title: String, message: String) async -> Bool {
        let alert = NSAlert()
        alert.messageText = title
        alert.informativeText = message
        alert.alertStyle = .warning
        alert.addButton(withTitle: "Yes")
        alert.addButton(withTitle: "No")

        return alert.runModal() == .alertFirstButtonReturn
    }
}
