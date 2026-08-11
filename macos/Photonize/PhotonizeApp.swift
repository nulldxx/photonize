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
                // Let the terminate guard read the live view model's state.
                .onAppear { appDelegate.viewModel = viewModel }
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
        }
        .defaultSize(width: 1200, height: 800)

        Settings {
            SettingsView()
                .environmentObject(viewModel)
        }
    }
}

class AppDelegate: NSObject, NSApplicationDelegate {
    /// Set from the window's `onAppear`. Weak so the delegate never keeps the
    /// view model alive past the app.
    weak var viewModel: PhotoGridViewModel?

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool {
        return true
    }

    /// Warn before quitting with an un-applied reorder, mirroring the Windows
    /// `MainWindow_Closing` guard. Deleting does not arm `hasUnsavedChanges`, so
    /// trashing a photo never triggers this.
    func applicationShouldTerminate(_ sender: NSApplication) -> NSApplication.TerminateReply {
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
