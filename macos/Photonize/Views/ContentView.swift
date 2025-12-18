import SwiftUI

struct ContentView: View {
    @EnvironmentObject var viewModel: PhotoGridViewModel
    @State private var previewWidth: CGFloat = 400

    var body: some View {
        VStack(spacing: 0) {
            // Toolbar
            ToolbarView()
                .padding()
                .background(Color(NSColor.controlBackgroundColor))

            Divider()

            // Main content
            GeometryReader { geometry in
                HStack(spacing: 0) {
                    // Photo grid
                    PhotoGridView()
                        .frame(width: viewModel.isPreviewVisible ? geometry.size.width - previewWidth : geometry.size.width)

                    // Preview pane (if visible)
                    if viewModel.isPreviewVisible {
                        ResizableDivider(
                            orientation: .vertical,
                            size: $previewWidth,
                            minSize: 300,
                            maxSize: geometry.size.width - 400
                        )

                        PreviewPane()
                            .frame(width: previewWidth)
                    }
                }
            }

            Divider()

            // Status bar
            StatusBarView()
                .padding(.horizontal)
                .padding(.vertical, 8)
                .background(Color(NSColor.controlBackgroundColor))
        }
    }
}

struct ResizableDivider: View {
    enum Orientation {
        case horizontal
        case vertical
    }

    let orientation: Orientation
    @Binding var size: CGFloat
    let minSize: CGFloat
    let maxSize: CGFloat

    @State private var isDragging = false
    @State private var startSize: CGFloat = 0

    var body: some View {
        ZStack {
            if orientation == .vertical {
                Rectangle()
                    .fill(Color(NSColor.separatorColor))
                    .frame(width: 1)
                    .frame(maxHeight: .infinity)
            } else {
                Rectangle()
                    .fill(Color(NSColor.separatorColor))
                    .frame(height: 1)
                    .frame(maxWidth: .infinity)
            }
        }
        .frame(width: orientation == .vertical ? 8 : nil, height: orientation == .horizontal ? 8 : nil)
        .background(isDragging ? Color.blue.opacity(0.1) : Color.clear)
        .contentShape(Rectangle())
        .gesture(
            DragGesture()
                .onChanged { value in
                    if !isDragging {
                        isDragging = true
                        startSize = size
                    }
                    if orientation == .vertical {
                        let newSize = startSize - value.translation.width
                        size = min(max(newSize, minSize), maxSize)
                    } else {
                        let newSize = startSize - value.translation.height
                        size = min(max(newSize, minSize), maxSize)
                    }
                }
                .onEnded { _ in
                    isDragging = false
                }
        )
        .onHover { hovering in
            if hovering {
                NSCursor.resizeLeftRight.push()
            } else {
                NSCursor.pop()
            }
        }
    }
}

struct ToolbarView: View {
    @EnvironmentObject var viewModel: PhotoGridViewModel

    var body: some View {
        VStack(spacing: 12) {
            HStack {
                // Directory selection
                TextField("Directory Path", text: $viewModel.directoryPath)
                    .textFieldStyle(.roundedBorder)
                    .disabled(true)

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
                .help("Toggle Preview")
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

            if !viewModel.selectedPhotos.isEmpty {
                Text("\(viewModel.selectedPhotos.count) selected")
                    .font(.system(size: 12))
                    .foregroundColor(.secondary)
            }
        }
    }
}
