import SwiftUI

@main
struct PhotonizeApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var appDelegate
    @StateObject private var viewModel = PhotoGridViewModel()

    var body: some Scene {
        WindowGroup {
            ContentView()
                .environmentObject(viewModel)
                .frame(minWidth: 800, minHeight: 600)
                // Hand the delegate the live view model: the terminate guard reads
                // its state, and any folder/export intent that arrived before the
                // window existed is delivered now (slice 11).
                .onAppear { appDelegate.attach(viewModel) }
        }
        .commands {
            // Replace the built-in "New" items so our commands land in the
            // real File menu instead of adding a second one.
            CommandGroup(replacing: .newItem) {
                Button("Open Folder...") {
                    viewModel.browseDirectory()
                }
                .keyboardShortcut("o", modifiers: .command)

                Divider()

                Button("Refresh") {
                    Task {
                        await viewModel.loadPhotos()
                    }
                }
                .keyboardShortcut("r", modifiers: .command)
                .disabled(viewModel.directoryPath.isEmpty)

                Divider()

                Button("Delete") {
                    Task {
                        await viewModel.deleteSelectedPhotos()
                    }
                }
                .keyboardShortcut(.delete, modifiers: [])
                .disabled(viewModel.selectedPhotos.isEmpty)
            }

            // Surface Select All (files only) in the Edit menu with its shortcut.
            // Replacing `.textEditing` keeps the standard Cut/Copy/Paste group.
            CommandGroup(replacing: .textEditing) {
                Button("Select All") {
                    viewModel.selectAllFiles()
                }
                .keyboardShortcut("a", modifiers: .command)
                .disabled(viewModel.photos.filter { !$0.isFolder }.isEmpty)
            }

            // View ▸ Compare Selected, so the compare tool is discoverable beyond
            // the right-click menu.
            CompareCommands(viewModel: viewModel)
        }
        .defaultSize(width: 1200, height: 800)

        // The compare window. A data-driven WindowGroup reuses one window per
        // pair, so comparing the same two photos twice brings the window forward
        // rather than stacking a second one.
        WindowGroup(id: "compare", for: ComparePair.self) { $pair in
            if let pair {
                ImageCompareView(pair: pair)
            }
        }
        .defaultSize(width: 1200, height: 800)   // matches CompareWindow.xaml

        Settings {
            SettingsView()
                .environmentObject(viewModel)
        }
    }
}

/// The View ▸ Compare Selected item. A dedicated `Commands` struct so it can read
/// `openWindow` from the environment, which the plain `.commands` closure cannot.
struct CompareCommands: Commands {
    @ObservedObject var viewModel: PhotoGridViewModel
    @Environment(\.openWindow) private var openWindow

    var body: some Commands {
        CommandGroup(after: .sidebar) {
            Button("Compare Selected") {
                if let pair = viewModel.comparePair {
                    openWindow(id: "compare", value: pair)
                }
            }
            .keyboardShortcut("c", modifiers: [.command, .shift])
            .disabled(!viewModel.canCompare)
        }
    }
}

/// A launch- or open-time request the app should act on. Parsed from the command
/// line before the window exists, or derived from `application(_:open:)` and the
/// Finder Services while the app runs (slice 11).
enum LaunchIntent: Equatable {
    case none
    /// Navigate to this folder.
    case openDirectory(URL)
    /// Navigate to the images' containing folder and pre-select them.
    case openImages([URL])
    /// Export these images straight to WebP, then reveal the output folder.
    case exportImages([URL])

    /// Whether this intent means "don't restore the saved folder at launch" —
    /// true for anything that sets its own directory.
    var suppressesDirectoryRestore: Bool {
        self != .none
    }
}

class AppDelegate: NSObject, NSApplicationDelegate {
    /// Set from the window's `onAppear`. Weak so the delegate never keeps the
    /// view model alive past the app.
    weak var viewModel: PhotoGridViewModel?

    /// An intent that arrived (via `application(_:open:)` or a Service) before the
    /// window and its view model existed. Delivered once `attach(_:)` links them.
    private var deferredIntent: LaunchIntent?

    // MARK: - Command line

    /// Parse the launch arguments before the first window appears, so the view
    /// model can start on the right folder rather than loading the saved one and
    /// then jumping. Unknown arguments are ignored, not fatal — Xcode injects
    /// `-NSDocumentRevisionsDebugMode YES` and similar into debug runs.
    func applicationWillFinishLaunching(_ notification: Notification) {
        PhotoGridViewModel.pendingLaunchIntent = Self.parseLaunchArguments(CommandLine.arguments)
    }

    /// Pure argument parser, matching the Windows `App.OnStartup` semantics:
    /// `-d`/`--directory <path>` opens a folder; `-e`/`--export-webp <file…>`
    /// exports the remaining files. Existence/type filtering is deferred to the
    /// export path, so this stays pure and testable.
    static func parseLaunchArguments(_ args: [String]) -> LaunchIntent {
        var index = 0
        while index < args.count {
            let arg = args[index]
            if (arg == "-d" || arg == "--directory"), index + 1 < args.count {
                return .openDirectory(URL(fileURLWithPath: args[index + 1]))
            }
            if arg == "-e" || arg == "--export-webp" {
                let files = args[(index + 1)...].map { URL(fileURLWithPath: $0) }
                return files.isEmpty ? .none : .exportImages(Array(files))
            }
            index += 1
        }
        return .none
    }

    // MARK: - Open / Dock / Services

    /// Register the Finder Services provider once the app has launched. The
    /// `NSServices` entries in Info.plist route to the `@objc` handlers below.
    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.servicesProvider = self
        NSUpdateDynamicServices()
    }

    /// `open -a Photonize <path>`, dragging onto the Dock icon or the app in
    /// Finder, and "Open With" all route here. A folder navigates; images are
    /// treated as a selection in their containing folder; a mix takes the first
    /// folder.
    func application(_ sender: NSApplication, open urls: [URL]) {
        deliver(Self.intent(forOpening: urls))
    }

    /// Classify opened URLs into an intent: the first folder wins; otherwise the
    /// images are opened as a selection.
    static func intent(forOpening urls: [URL]) -> LaunchIntent {
        if let folder = urls.first(where: isDirectory) {
            return .openDirectory(folder)
        }
        let images = urls.filter { !isDirectory($0) }
        return images.isEmpty ? .none : .openImages(images)
    }

    private static func isDirectory(_ url: URL) -> Bool {
        var isDir: ObjCBool = false
        return FileManager.default.fileExists(atPath: url.path, isDirectory: &isDir) && isDir.boolValue
    }

    /// Finder ▸ Services ▸ Open in Photonize. The pasteboard carries the selected
    /// folder(s).
    @objc func openFolderInPhotonize(_ pboard: NSPasteboard, userData: String, error: AutoreleasingUnsafeMutablePointer<NSString>) {
        let urls = pboard.readObjects(forClasses: [NSURL.self], options: nil) as? [URL] ?? []
        guard let folder = urls.first(where: Self.isDirectory) ?? urls.first else { return }
        deliver(.openDirectory(folder))
    }

    /// Finder ▸ Services ▸ Export to WebP. The pasteboard hands us every selected
    /// image in one call, so multi-file export works from a single app instance
    /// (unlike the Windows verb, which spawns one instance per file).
    @objc func exportToWebP(_ pboard: NSPasteboard, userData: String, error: AutoreleasingUnsafeMutablePointer<NSString>) {
        let urls = pboard.readObjects(forClasses: [NSURL.self], options: nil) as? [URL] ?? []
        guard !urls.isEmpty else { return }
        deliver(.exportImages(urls))
    }

    /// Link the live view model and flush any intent that arrived before it
    /// existed (a cold open/Service that beat the window to the screen).
    func attach(_ viewModel: PhotoGridViewModel) {
        self.viewModel = viewModel
        if let intent = deferredIntent {
            deferredIntent = nil
            Task { @MainActor in await viewModel.applyLaunchIntent(intent) }
        }
    }

    /// Route an intent into the existing window — never a second one. If the view
    /// model is live, apply now and bring the window forward; otherwise stash it
    /// for `attach(_:)`.
    private func deliver(_ intent: LaunchIntent) {
        NSApp.activate(ignoringOtherApps: true)
        guard let viewModel else {
            deferredIntent = intent
            return
        }
        NSApp.windows.first?.makeKeyAndOrderFront(nil)
        Task { @MainActor in await viewModel.applyLaunchIntent(intent) }
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool {
        return true
    }

    /// Warn before quitting with an un-applied reorder, mirroring the Windows
    /// `MainWindow_Closing` guard. Deleting does not arm `hasUnsavedChanges`, so
    /// trashing a photo never triggers this.
    func applicationShouldTerminate(_ sender: NSApplication) -> NSApplication.TerminateReply {
        // Land any pending caption edit before we consider quitting, so a ⌘Q
        // straight after typing never loses the last few keystrokes (slice 09).
        viewModel?.flushPromptSave()

        guard let viewModel, viewModel.hasUnsavedChanges else { return .terminateNow }

        let alert = NSAlert()
        alert.messageText = "Unsaved Changes"
        alert.informativeText = "You have reordered photos without applying the rename. Quit anyway?"
        alert.alertStyle = .warning
        alert.addButton(withTitle: "Quit")
        alert.addButton(withTitle: "Cancel")

        return alert.runModal() == .alertFirstButtonReturn ? .terminateNow : .terminateCancel
    }
}
