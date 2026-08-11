import SwiftUI

/// The app's Settings scene (⌘,). Home for preferences that would otherwise
/// clutter the toolbar; later slices add their options here.
struct SettingsView: View {
    @EnvironmentObject var viewModel: PhotoGridViewModel

    var body: some View {
        Form {
            Section("Thumbnails") {
                HStack {
                    Text("Default size")
                    Slider(value: $viewModel.thumbnailSize, in: 100...400, step: 50)
                    Text("\(Int(viewModel.thumbnailSize))px")
                        .frame(width: 50, alignment: .trailing)
                        .monospacedDigit()
                }
            }

            Section("On launch") {
                Toggle("Restore last folder on launch", isOn: $viewModel.restoreLastFolderOnLaunch)
            }
        }
        .formStyle(.grouped)
        .frame(width: 420)
        .padding()
    }
}
