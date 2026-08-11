import SwiftUI

/// The prompt/caption editor tab of the inspector (slice 09). It edits the `.txt`
/// sidecar of the last-touched image; loading, debounced auto-save and empty-file
/// cleanup all live in the view model, so this view is purely the editing surface.
struct PromptEditorPane: View {
    @EnvironmentObject var viewModel: PhotoGridViewModel

    var body: some View {
        VStack(spacing: 0) {
            if let image = viewModel.promptImage {
                header(for: image)
                Divider()
                editor
            } else {
                unavailableState
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
    }

    /// `<basename>.txt`, matching the Windows pane header so it's obvious which
    /// file is being edited.
    private func header(for image: PhotoItem) -> some View {
        HStack(spacing: 6) {
            Image(systemName: "doc.plaintext")
                .foregroundColor(.secondary)
            Text(PromptFileStore.url(for: image.filePath).lastPathComponent)
                .font(.system(size: 11, weight: .semibold))
                .foregroundColor(.secondary)
                .lineLimit(1)
                .truncationMode(.middle)
            Spacer()
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 8)
        .background(Color(NSColor.controlBackgroundColor))
    }

    /// SF Mono is the native counterpart to Windows' Consolas; asking for it by
    /// the `.monospaced` design token (not by name) lets it follow the user's
    /// system monospace choice.
    private var editor: some View {
        ZStack(alignment: .topLeading) {
            TextEditor(text: $viewModel.promptText)
                .font(.system(.body, design: .monospaced))
                .padding(4)

            // A live pane with no caption yet reads as ready to type, rather than
            // looking like the empty "nothing selected" state. Non-interactive so
            // it never intercepts the first keystroke.
            if viewModel.promptText.isEmpty {
                Text("No prompt yet — start typing")
                    .font(.system(.body, design: .monospaced))
                    .foregroundColor(Color(NSColor.placeholderTextColor))
                    .padding(.horizontal, 9)
                    .padding(.vertical, 12)
                    .allowsHitTesting(false)
            }
        }
    }

    private var unavailableState: some View {
        VStack(spacing: 12) {
            Image(systemName: iconName)
                .font(.system(size: 48))
                .foregroundColor(.gray)
            Text(message)
                .font(.headline)
                .foregroundColor(.gray)
                .multilineTextAlignment(.center)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .padding()
    }

    /// Distinguish "nothing selected" from "this thing has no prompt": a folder or
    /// video is a live selection that simply has no caption file, which is a
    /// different situation from an empty inspector.
    private var message: String {
        switch viewModel.previewPhoto?.kind {
        case .folder, .parent: return "Folders don't have prompts."
        case .video:           return "Videos don't have prompts."
        default:               return "No image selected."
        }
    }

    private var iconName: String {
        switch viewModel.previewPhoto?.kind {
        case .folder, .parent: return "folder"
        case .video:           return "film"
        default:               return "photo.on.rectangle"
        }
    }
}
