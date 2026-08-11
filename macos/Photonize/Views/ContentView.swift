import SwiftUI
import AppKit

/// Which inspector tab is showing. Persisted via `isTextEditorVisible` so the
/// last-used tab is restored when the inspector is reopened.
enum InspectorTab: Hashable {
    case preview, text
}

struct ContentView: View {
    @EnvironmentObject var viewModel: PhotoGridViewModel

    var body: some View {
        VStack(spacing: 0) {
            // Toolbar
            ToolbarView()
                .padding()
                .background(Color(NSColor.controlBackgroundColor))

            Divider()

            // Main content
            if viewModel.isPreviewVisible {
                HSplitView {
                    PhotoGridView()
                        .frame(minWidth: 400)

                    InspectorPane()
                        .frame(minWidth: 300, idealWidth: 400, maxWidth: .infinity)
                }
            } else {
                PhotoGridView()
            }

            Divider()

            // Status bar
            StatusBarView()
                .padding(.horizontal)
                .padding(.vertical, 8)
                .background(Color(NSColor.controlBackgroundColor))
        }
        // Flush any pending caption edit when the window loses key focus (e.g. the
        // user clicks another app), a cheap safety net beyond the debounce.
        .onReceive(NotificationCenter.default.publisher(for: NSWindow.didResignKeyNotification)) { _ in
            viewModel.flushPromptSave()
        }
        // Modal-to-the-window progress for export/import/tag-move batches.
        .sheet(item: $viewModel.operationProgress) { progress in
            ProgressSheet(progress: progress)
        }
        // Insert-vs-distribute choice for a Finder drop. Presented before the
        // progress sheet, so the two are never up at once.
        .sheet(item: $viewModel.importModeRequest) { request in
            ImportModeSheet(request: request)
        }
        // Per-tag sub-folder names for Tags ▸ Move tagged images. Resolves before
        // the progress sheet, so the two are never up at once.
        .sheet(item: $viewModel.tagFolderRequest) { request in
            TagFolderSheet(request: request)
        }
    }
}

/// The right-hand inspector: a segmented Preview/Text switch over the two tabs.
/// Where Windows uses two 500 px panes with two toolbar toggles that close each
/// other, the Mac client keeps one pane, one visibility toggle, and a tab picker
/// — both surfaces are one click away and the layout never shuffles (plans/09).
struct InspectorPane: View {
    @EnvironmentObject var viewModel: PhotoGridViewModel

    var body: some View {
        VStack(spacing: 0) {
            Picker("", selection: Binding(
                get: { viewModel.isTextEditorVisible ? InspectorTab.text : .preview },
                set: { viewModel.isTextEditorVisible = ($0 == .text) }
            )) {
                Text("Preview").tag(InspectorTab.preview)
                Text("Text").tag(InspectorTab.text)
            }
            .pickerStyle(.segmented)
            .labelsHidden()
            .padding(8)

            Divider()

            if viewModel.isTextEditorVisible {
                PromptEditorPane()
            } else {
                PreviewPane()
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
    }
}

struct ToolbarView: View {
    @EnvironmentObject var viewModel: PhotoGridViewModel

    var body: some View {
        VStack(spacing: 12) {
            HStack {
                // Editable path: type or paste a folder and press Return to load it.
                TextField("Directory Path", text: $viewModel.directoryPath)
                    .textFieldStyle(.roundedBorder)
                    .onSubmit {
                        viewModel.submitDirectoryPath()
                    }

                Button("Browse...") {
                    viewModel.browseDirectory()
                }

                Button(action: {
                    Task {
                        await viewModel.loadPhotos()
                    }
                }) {
                    Image(systemName: "arrow.clockwise")
                }
                .disabled(viewModel.directoryPath.isEmpty)
                .help("Refresh")

                Spacer()

                Button(action: {
                    viewModel.isPreviewVisible.toggle()
                }) {
                    Image(systemName: viewModel.isPreviewVisible ? "sidebar.right" : "sidebar.left")
                }
                .help("Toggle Inspector")
            }

            if !viewModel.directoryPath.isEmpty {
                BreadcrumbView(path: viewModel.directoryPath)
            }

            HStack {
                Text("Rename Prefix:")
                    .frame(width: 100, alignment: .trailing)

                TextField("Enter prefix", text: $viewModel.renamePrefix)
                    .textFieldStyle(.roundedBorder)
                    .onChange(of: viewModel.renamePrefix) { _ in
                        viewModel.hasUnsavedChanges = !viewModel.photos.filter({ !$0.isFolder }).isEmpty && !viewModel.renamePrefix.isEmpty
                    }

                Button("Apply Rename") {
                    Task {
                        await viewModel.applyRename()
                    }
                }
                .disabled(!canRename())

                Spacer()

                Text("Thumbnail Size:")
                Slider(value: $viewModel.thumbnailSize, in: 100...400, step: 50)
                    .frame(width: 150)

                Text("\(Int(viewModel.thumbnailSize))px")
                    .frame(width: 50)
            }
        }
    }

    private func canRename() -> Bool {
        !viewModel.photos.filter({ !$0.isFolder }).isEmpty &&
        !viewModel.renamePrefix.isEmpty &&
        viewModel.hasUnsavedChanges
    }
}

/// Clickable path breadcrumb — a native alternative to climbing with `..` one
/// level at a time. Each component jumps straight to that ancestor.
struct BreadcrumbView: View {
    @EnvironmentObject var viewModel: PhotoGridViewModel
    let path: String

    var body: some View {
        let crumbs = Self.crumbs(for: path)
        ScrollView(.horizontal, showsIndicators: false) {
            HStack(spacing: 4) {
                ForEach(Array(crumbs.enumerated()), id: \.element.path) { index, crumb in
                    if index == crumbs.count - 1 {
                        // Current folder: not a navigation target.
                        Text(crumb.name)
                            .font(.system(size: 11, weight: .semibold))
                            .foregroundColor(.primary)
                    } else {
                        Button(crumb.name) {
                            viewModel.navigateToPath(crumb.path)
                        }
                        .buttonStyle(.link)
                        .font(.system(size: 11))

                        Text("›")
                            .font(.system(size: 11))
                            .foregroundColor(.secondary)
                    }
                }
            }
        }
    }

    /// Split a path into (display name, absolute path) pairs, e.g.
    /// "/Users/ben/Trip" → [("/", "/"), ("Users", "/Users"), …].
    static func crumbs(for path: String) -> [(name: String, path: String)] {
        guard !path.isEmpty else { return [] }
        let components = URL(fileURLWithPath: path).standardizedFileURL.pathComponents
        var result: [(name: String, path: String)] = []
        var accumulated = URL(fileURLWithPath: "/")
        for component in components {
            if component == "/" {
                result.append((name: "/", path: "/"))
            } else {
                accumulated.appendPathComponent(component)
                result.append((name: component, path: accumulated.path))
            }
        }
        return result
    }
}

struct StatusBarView: View {
    @EnvironmentObject var viewModel: PhotoGridViewModel

    var body: some View {
        HStack {
            if viewModel.isLoading {
                ProgressView()
                    .scaleEffect(0.7)
                    .frame(width: 20, height: 20)
            }

            Text(viewModel.statusMessage)
                .font(.system(size: 12))
                .foregroundColor(.secondary)

            Spacer()

            Text("Photos: \(viewModel.photos.filter { !$0.isFolder }.count)")
                .font(.system(size: 12))
                .foregroundColor(.secondary)

            if !viewModel.selectedPhotos.isEmpty {
                Text("\(viewModel.selectedPhotos.count) selected")
                    .font(.system(size: 12))
                    .foregroundColor(.secondary)
            }
        }
    }
}
