import SwiftUI
import UniformTypeIdentifiers

extension View {
    @ViewBuilder
    func `if`<Transform: View>(_ condition: Bool, transform: (Self) -> Transform) -> some View {
        if condition {
            transform(self)
        } else {
            self
        }
    }
}

struct PhotoGridView: View {
    @EnvironmentObject var viewModel: PhotoGridViewModel
    @State private var draggingItem: PhotoItem?

    private let columns = [GridItem(.adaptive(minimum: 150, maximum: 400), spacing: 16)]

    var body: some View {
        ScrollView {
            LazyVGrid(columns: columns, spacing: 16) {
                ForEach(viewModel.photos) { photo in
                    PhotoItemView(photo: photo, draggingItem: $draggingItem)
                        .contextMenu {
                            PhotoContextMenu(photo: photo)
                        }
                        .if(!photo.isFolder) { view in
                            view
                                .onDrag {
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
            }
            .padding()
        }
        .background(Color(NSColor.windowBackgroundColor))
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
        guard let draggingItem = draggingItem else { return }

        if draggingItem.id != item.id {
            let from = items.firstIndex(of: draggingItem)!
            let to = items.firstIndex(of: item)!

            withAnimation(.default) {
                items.move(fromOffsets: IndexSet(integer: from), toOffset: to > from ? to + 1 : to)
            }
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
                        Image(systemName: "photo")
                            .font(.system(size: 40))
                            .foregroundColor(.gray)
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
        .onTapGesture {
            handleSelection()
        }
        .onTapGesture(count: 2) {
            viewModel.openPhoto(photo)
        }
    }

    private func handleSelection() {
        if NSEvent.modifierFlags.contains(.command) {
            // Command+Click: Toggle selection
            if viewModel.selectedPhotos.contains(photo.id) {
                viewModel.selectedPhotos.remove(photo.id)
            } else {
                viewModel.selectedPhotos.insert(photo.id)
            }
        } else if NSEvent.modifierFlags.contains(.shift) {
            // Shift+Click: Range selection
            // For simplicity, just add to selection
            viewModel.selectedPhotos.insert(photo.id)
        } else {
            // Regular click: Single selection
            viewModel.selectedPhotos = [photo.id]
        }

        // Update preview
        if let firstSelected = viewModel.photos.first(where: { viewModel.selectedPhotos.contains($0.id) }) {
            viewModel.previewPhoto = firstSelected
        }
    }
}

struct PhotoContextMenu: View {
    @EnvironmentObject var viewModel: PhotoGridViewModel
    let photo: PhotoItem

    var body: some View {
        Button("Open") {
            viewModel.openPhoto(photo)
        }

        Button("Show in Finder") {
            viewModel.showInFinder(photo)
        }

        Divider()

        Button("Delete") {
            // Ensure this photo is selected
            if !viewModel.selectedPhotos.contains(photo.id) {
                viewModel.selectedPhotos = [photo.id]
            }

            Task {
                await viewModel.deleteSelectedPhotos()
            }
        }
        .disabled(photo.isFolder)
    }
}
