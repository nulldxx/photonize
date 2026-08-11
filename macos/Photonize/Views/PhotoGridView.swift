import SwiftUI
import UniformTypeIdentifiers

struct PhotoGridView: View {
    @EnvironmentObject var viewModel: PhotoGridViewModel
    @State private var draggingItem: PhotoItem?

    // Layout constants. The column count is derived from these so that
    // keyboard row-wrapping matches what the grid actually draws.
    private static let gridSpacing: CGFloat = 16
    private static let cellPadding: CGFloat = 16   // PhotoItemView's 8pt on each side
    private static let outerPadding: CGFloat = 16  // grid inset, per edge

    var body: some View {
        GeometryReader { geo in
            ScrollViewReader { proxy in
                ScrollView {
                    LazyVGrid(columns: columns(forWidth: geo.size.width), spacing: Self.gridSpacing) {
                        ForEach(viewModel.photos) { photo in
                            PhotoItemView(photo: photo, draggingItem: $draggingItem)
                                .id(photo.id)
                                .contextMenu {
                                    PhotoContextMenu(photo: photo)
                                }
                                .onDrag {
                                    // Folders are not drag sources yet (slice 06 makes
                                    // them move targets); hand back an empty provider so
                                    // no reorder starts, but keep the cell a drop target.
                                    guard !photo.isFolder else { return NSItemProvider() }
                                    draggingItem = photo
                                    return NSItemProvider(object: photo.id.uuidString as NSString)
                                }
                                .onDrop(of: [.text], delegate: PhotoDropDelegate(
                                    item: photo,
                                    items: $viewModel.photos,
                                    draggingItem: $draggingItem,
                                    onDrop: {
                                        viewModel.updateDisplayOrder()
                                    }
                                ))
                        }
                    }
                    .padding(Self.outerPadding)
                }
                .background(KeyCatcher { viewModel.handleKey($0) })
                .background(Color(NSColor.windowBackgroundColor))
                .onChange(of: viewModel.focusedPhotoID) { id in
                    guard let id else { return }
                    withAnimation { proxy.scrollTo(id, anchor: .center) }
                }
            }
            .onAppear { viewModel.columnCount = columnCount(forWidth: geo.size.width) }
            .onChange(of: geo.size.width) { viewModel.columnCount = columnCount(forWidth: $0) }
        }
    }

    private func cellWidth() -> CGFloat {
        viewModel.thumbnailSize + Self.cellPadding
    }

    /// Number of columns that fit at `width`. Floors so each column is at least a
    /// cell wide (content never clips); building exactly this many `.flexible`
    /// columns means the view model's count matches the drawn layout.
    private func columnCount(forWidth width: CGFloat) -> Int {
        let available = width - Self.outerPadding * 2
        let cell = cellWidth()
        guard available > 0, cell > 0 else { return 1 }
        return max(1, Int((available + Self.gridSpacing) / (cell + Self.gridSpacing)))
    }

    private func columns(forWidth width: CGFloat) -> [GridItem] {
        Array(
            repeating: GridItem(.flexible(), spacing: Self.gridSpacing),
            count: columnCount(forWidth: width)
        )
    }
}

struct PhotoDropDelegate: DropDelegate {
    let item: PhotoItem
    @Binding var items: [PhotoItem]
    @Binding var draggingItem: PhotoItem?
    let onDrop: () -> Void

    func performDrop(info: DropInfo) -> Bool {
        draggingItem = nil
        onDrop()
        return true
    }

    func dropEntered(info: DropInfo) {
        guard let draggingItem, draggingItem.id != item.id else { return }
        // No folder reordering in this slice; a folder target is inert (but the
        // drag still passes over it rather than being blocked).
        guard !draggingItem.isFolder, !item.isFolder else { return }

        // Guard the lookups: the dragged item can vanish mid-drag (a delete, or a
        // Finder move) and force-unwrapping would crash.
        guard let from = items.firstIndex(of: draggingItem),
              let to = items.firstIndex(of: item) else { return }

        withAnimation(.default) {
            items.move(fromOffsets: IndexSet(integer: from), toOffset: to > from ? to + 1 : to)
        }
    }
}

struct PhotoItemView: View {
    @EnvironmentObject var viewModel: PhotoGridViewModel
    let photo: PhotoItem
    @Binding var draggingItem: PhotoItem?

    var body: some View {
        VStack(spacing: 4) {
            if let thumbnail = photo.thumbnail {
                Image(nsImage: thumbnail)
                    .resizable()
                    .aspectRatio(contentMode: .fit)
                    .frame(width: viewModel.thumbnailSize, height: viewModel.thumbnailSize)
                    .cornerRadius(8)
            } else {
                Rectangle()
                    .fill(Color.gray.opacity(0.3))
                    .frame(width: viewModel.thumbnailSize, height: viewModel.thumbnailSize)
                    .cornerRadius(8)
                    .overlay(
                        // Subtle progress treatment so a slow thumbnail reads as
                        // loading rather than broken.
                        VStack(spacing: 8) {
                            Image(systemName: "photo")
                                .font(.system(size: 40))
                                .foregroundColor(.gray)
                            ProgressView()
                                .controlSize(.small)
                        }
                    )
            }

            Text(photo.fileName)
                .font(.system(size: 11))
                .lineLimit(2)
                .multilineTextAlignment(.center)
                .frame(width: viewModel.thumbnailSize)
        }
        .padding(8)
        .background(
            RoundedRectangle(cornerRadius: 8)
                .fill(viewModel.selectedPhotos.contains(photo.id) ? Color.accentColor.opacity(0.2) : Color.clear)
        )
        .overlay(
            RoundedRectangle(cornerRadius: 8)
                .stroke(viewModel.selectedPhotos.contains(photo.id) ? Color.accentColor : Color.clear, lineWidth: 2)
        )
        .opacity(draggingItem?.id == photo.id ? 0.5 : 1.0)
        // Double-tap must win over the selection tap, or double-click-to-open is
        // unreliable — the earlier code attached them in the order that let the
        // single tap swallow the double.
        .gesture(
            TapGesture(count: 2)
                .onEnded { viewModel.openPhoto(photo) }
                .exclusively(before:
                    TapGesture()
                        .onEnded {
                            viewModel.handleClick(
                                on: photo,
                                commandKey: NSEvent.modifierFlags.contains(.command),
                                shiftKey: NSEvent.modifierFlags.contains(.shift)
                            )
                        }
                )
        )
    }
}

/// The right-click menu, built from the item's `kind`. Each case lists only the
/// entries in the slice-04 table; later slices drop their items at the marked
/// insertion points, keeping this a one-line change for them.
struct PhotoContextMenu: View {
    @EnvironmentObject var viewModel: PhotoGridViewModel
    let photo: PhotoItem

    @ViewBuilder
    var body: some View {
        switch photo.kind {
        case .parent:
            // The ".." tile only navigates; it cannot be shown-in-Finder or deleted.
            openButton

        case .folder:
            openButton
            showInFinderButton
            // INSERTION POINT — slice 05: Export ▸ (PhotoAI Upscale Folder)
            // INSERTION POINT — slice 10: Tags ▸
            Divider()
            deleteButton

        case .video:
            openButton
            showInFinderButton
            // INSERTION POINT — slice 10: Tags ▸
            Divider()
            deleteButton

        case .image:
            openButton
            showInFinderButton
            // INSERTION POINT — slice 07: Image Compare
            // INSERTION POINT — slice 05: Export ▸
            // INSERTION POINT — slice 10: Tags ▸
            Divider()
            deleteButton
        }
    }

    private var openButton: some View {
        Button("Open") { viewModel.openPhoto(photo) }
    }

    private var showInFinderButton: some View {
        Button("Show in Finder") { viewModel.showInFinder(photo) }
    }

    private var deleteButton: some View {
        Button("Delete") {
            // Windows rule: a right-click on an item outside the current
            // selection acts on that item alone.
            if !viewModel.selectedPhotos.contains(photo.id) {
                viewModel.selectedPhotos = [photo.id]
            }
            Task {
                await viewModel.deleteSelectedPhotos()
            }
        }
    }
}
