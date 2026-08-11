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
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool {
        return true
    }
}
