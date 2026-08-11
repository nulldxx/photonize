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
    @Published var previewPhoto: PhotoItem?
    @Published var hasUnsavedChanges: Bool = false

    private let fileRenamer = FileRenamer()
    private let thumbnailGenerator = ThumbnailGenerator()
    private let thumbnailLoader = ThumbnailLoader()
    private let settingsManager = SettingsManager()
    private let directoryAccess = DirectoryAccess()

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

    init() {
        loadSettings()
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
        if let scopedURL {
            directoryAccess.relinquish(scopedURL)
        }
        scopedURL = url
        directoryBookmark = directoryAccess.makeBookmark(for: url)
        directoryPath = url.path
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

            let imageFiles = contents.filter { url in
                guard (try? url.resourceValues(forKeys: [.isDirectoryKey]))?.isDirectory == false else {
                    return false
                }
                return thumbnailGenerator.isImageFile(url)
            }.sorted { $0.lastPathComponent < $1.lastPathComponent }

            for imageFile in imageFiles {
                let photoItem = PhotoItem(
                    filePath: imageFile,
                    fileName: imageFile.lastPathComponent,
                    displayOrder: displayOrder,
                    isFolder: false,
                    thumbnail: nil
                )
                newPhotos.append(photoItem)
                displayOrder += 1
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

        guard !renamePrefix.trimmingCharacters(in: .whitespaces).isEmpty else {
            showAlert(title: "Warning", message: "Please enter a prefix for the files.")
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

        isLoading = true
        statusMessage = "Renaming files..."

        let result = await fileRenamer.renameFiles(photoItems, prefix: renamePrefix)

        statusMessage = result.message
        isLoading = false

        if result.success {
            // Update photo items with new paths and names
            for (index, oldItem) in photoItems.enumerated() {
                if let photoIndex = photos.firstIndex(where: { $0.id == oldItem.id }) {
                    let fileExtension = oldItem.filePath.pathExtension
                    let newFileName = "\(renamePrefix)-\(String(format: "%05d", index + 1)).\(fileExtension)"
                    let newPath = oldItem.filePath.deletingLastPathComponent().appendingPathComponent(newFileName)

                    photos[photoIndex].filePath = newPath
                    photos[photoIndex].fileName = newFileName
                }
            }

            hasUnsavedChanges = false
        } else {
            showAlert(title: "Error", message: result.message)
        }
    }

    func deleteSelectedPhotos() async {
        let photosToDelete = photos.filter { selectedPhotos.contains($0.id) && !$0.isFolder }

        guard !photosToDelete.isEmpty else { return }

        // Check if we can use Trash (fails on network drives)
        let canUseTrash = await checkIfCanUseTrash(for: photosToDelete.first!)

        let message: String
        let title: String

        if canUseTrash {
            title = "Move to Trash"
            if photosToDelete.count == 1 {
                message = "Are you sure you want to move this file to Trash?\n\n\(photosToDelete[0].fileName)"
            } else {
                message = "Are you sure you want to move \(photosToDelete.count) files to Trash?"
            }
        } else {
            title = "Permanently Delete"
            if photosToDelete.count == 1 {
                message = "This file is on a network drive where Trash is not available.\n\nAre you sure you want to PERMANENTLY delete this file?\n\n\(photosToDelete[0].fileName)\n\nThis action cannot be undone."
            } else {
                message = "These files are on a network drive where Trash is not available.\n\nAre you sure you want to PERMANENTLY delete \(photosToDelete.count) files?\n\nThis action cannot be undone."
            }
        }

        let confirmed = await showConfirmation(title: title, message: message)
        guard confirmed else { return }

        var deletedCount = 0
        var failedFiles: [String] = []

        for photo in photosToDelete {
            do {
                if canUseTrash {
                    // Try to move to Trash
                    try FileManager.default.trashItem(at: photo.filePath, resultingItemURL: nil)
                } else {
                    // Permanently delete
                    try FileManager.default.removeItem(at: photo.filePath)
                }

                if let index = photos.firstIndex(where: { $0.id == photo.id }) {
                    photos.remove(at: index)
                }
                deletedCount += 1
            } catch {
                failedFiles.append("\(photo.fileName): \(error.localizedDescription)")
            }
        }

        updateDisplayOrder()

        if deletedCount == 1 {
            statusMessage = canUseTrash ? "Moved \(photosToDelete[0].fileName) to Trash" : "Deleted \(photosToDelete[0].fileName)"
        } else {
            statusMessage = canUseTrash ? "Moved \(deletedCount) file(s) to Trash" : "Deleted \(deletedCount) file(s)"
        }

        if !failedFiles.isEmpty {
            showAlert(
                title: "Delete Errors",
                message: "Failed to delete \(failedFiles.count) file(s):\n\n\(failedFiles.joined(separator: "\n"))"
            )
        }

        selectedPhotos.removeAll()
    }

    private func checkIfCanUseTrash(for photo: PhotoItem) async -> Bool {
        do {
            // Try to get the trash URL for this volume
            let volumeURL = photo.filePath.deletingLastPathComponent()
            _ = try FileManager.default.url(for: .trashDirectory,
                                           in: .userDomainMask,
                                           appropriateFor: volumeURL,
                                           create: false)
            return true
        } catch {
            // Trash is not available (likely network drive)
            return false
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

    func updateDisplayOrder() {
        for (index, _) in photos.enumerated() {
            photos[index].displayOrder = index
        }
        hasUnsavedChanges = !photos.filter({ !$0.isFolder }).isEmpty && !renamePrefix.isEmpty
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

    private func loadSettings() {
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

        guard settings.restoreLastFolderOnLaunch else {
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
