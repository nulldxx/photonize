import Foundation
import SwiftUI
import UniformTypeIdentifiers

@MainActor
class PhotoGridViewModel: ObservableObject {
    @Published var photos: [PhotoItem] = []
    @Published var directoryPath: String = ""
    @Published var renamePrefix: String = ""
    @Published var thumbnailSize: Double = 200
    @Published var statusMessage: String = "Ready"
    @Published var isLoading: Bool = false
    @Published var selectedPhotos: Set<PhotoItem.ID> = []
    @Published var isPreviewVisible: Bool = false
    @Published var previewPhoto: PhotoItem?
    @Published var hasUnsavedChanges: Bool = false

    private let fileRenamer = FileRenamer()
    private let thumbnailGenerator = ThumbnailGenerator()
    private let settingsManager = SettingsManager()

    init() {
        loadSettings()
    }

    func browseDirectory() {
        let panel = NSOpenPanel()
        panel.canChooseFiles = false
        panel.canChooseDirectories = true
        panel.allowsMultipleSelection = false

        if panel.runModal() == .OK, let url = panel.url {
            directoryPath = url.path
            Task {
                await loadPhotos()
            }
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
        isLoading = true
        statusMessage = "Loading photos..."
        photos.removeAll()

        var displayOrder = 0
        var newPhotos: [PhotoItem] = []

        do {
            // Add parent directory navigation
            if let parentURL = dirURL.deletingLastPathComponent().path != "/" ? dirURL.deletingLastPathComponent() : nil {
                let parentItem = PhotoItem(
                    filePath: parentURL,
                    fileName: "..",
                    displayOrder: displayOrder,
                    isFolder: true,
                    thumbnail: thumbnailGenerator.generateFolderThumbnail(size: Int(thumbnailSize))
                )
                newPhotos.append(parentItem)
                displayOrder += 1
            }

            // Load subfolders
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
                    thumbnail: thumbnailGenerator.generateFolderThumbnail(size: Int(thumbnailSize))
                )
                newPhotos.append(folderItem)
                displayOrder += 1
            }

            // Load image files
            let imageFiles = contents.filter { url in
                guard (try? url.resourceValues(forKeys: [.isDirectoryKey]))?.isDirectory == false else {
                    return false
                }
                return thumbnailGenerator.isImageFile(url)
            }.sorted { $0.lastPathComponent < $1.lastPathComponent }

            for imageFile in imageFiles {
                let thumbnail = await thumbnailGenerator.generateThumbnail(for: imageFile, maxSize: Int(thumbnailSize))
                let photoItem = PhotoItem(
                    filePath: imageFile,
                    fileName: imageFile.lastPathComponent,
                    displayOrder: displayOrder,
                    isFolder: false,
                    thumbnail: thumbnail
                )
                newPhotos.append(photoItem)
                displayOrder += 1
            }

            photos = newPhotos
            detectCommonPrefix()

            let photoCount = photos.filter { !$0.isFolder }.count
            let folderCount = photos.filter { $0.isFolder && $0.fileName != ".." }.count
            statusMessage = "Loaded \(folderCount) folder(s) and \(photoCount) photo(s)"

            saveSettings()
            hasUnsavedChanges = false
        } catch {
            statusMessage = "Error loading photos: \(error.localizedDescription)"
        }

        isLoading = false
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

            saveSettings()
            hasUnsavedChanges = false
        } else {
            showAlert(title: "Error", message: result.message)
        }
    }

    func deleteSelectedPhotos() async {
        let photosToDelete = photos.filter { selectedPhotos.contains($0.id) && !$0.isFolder }

        guard !photosToDelete.isEmpty else { return }

        let message: String
        if photosToDelete.count == 1 {
            message = "Are you sure you want to permanently delete this file?\n\n\(photosToDelete[0].fileName)\n\nThis action cannot be undone."
        } else {
            message = "Are you sure you want to permanently delete \(photosToDelete.count) files?\n\nThis action cannot be undone."
        }

        let confirmed = await showConfirmation(title: "Confirm Delete", message: message)
        guard confirmed else { return }

        var deletedCount = 0
        var failedFiles: [String] = []

        for photo in photosToDelete {
            do {
                try FileManager.default.trashItem(at: photo.filePath, resultingItemURL: nil)
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
            statusMessage = "Deleted \(photosToDelete[0].fileName)"
        } else {
            statusMessage = "Deleted \(deletedCount) file(s)"
        }

        if !failedFiles.isEmpty {
            showAlert(
                title: "Delete Errors",
                message: "Failed to delete \(failedFiles.count) file(s):\n\n\(failedFiles.joined(separator: "\n"))"
            )
        }

        selectedPhotos.removeAll()
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

        if !settings.lastDirectoryPath.isEmpty && FileManager.default.fileExists(atPath: settings.lastDirectoryPath) {
            directoryPath = settings.lastDirectoryPath
            Task {
                await loadPhotos()
            }
        }

        thumbnailSize = settings.lastThumbnailSize
        if !settings.lastPrefix.isEmpty {
            renamePrefix = settings.lastPrefix
        }
        isPreviewVisible = settings.isPreviewVisible
    }

    private func saveSettings() {
        let settings = AppSettings(
            lastDirectoryPath: directoryPath,
            lastThumbnailSize: thumbnailSize,
            lastPrefix: renamePrefix,
            isPreviewVisible: isPreviewVisible
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
