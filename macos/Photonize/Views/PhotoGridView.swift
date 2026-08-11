import SwiftUI
import AppKit
import UniformTypeIdentifiers

extension UTType {
    /// The internal drag payload — grid items dragged within the app. Declared in
    /// Info.plist under UTExportedTypeDeclarations so it is a well-formed UTI. It
    /// carries the dragged selection's ids as JSON; external drags arrive as
    /// `.fileURL` instead, and the drop delegate branches on which one it sees.
    static var photoItem: UTType { UTType(exportedAs: "com.photonize.photo-item") }
}

extension NSItemProvider {
    /// Wrap a set of grid-item ids as the internal drag payload. Visibility is
    /// `.ownProcess` — this is only ever read back inside Photonize.
    convenience init(photoItemIDs ids: [UUID]) {
        self.init()
        let data = (try? JSONEncoder().encode(ids.map { $0.uuidString })) ?? Data()
        registerDataRepresentation(forTypeIdentifier: UTType.photoItem.identifier, visibility: .ownProcess) { completion in
            completion(data, nil)
            return nil
        }
    }
}

struct PhotoGridView: View {
    @EnvironmentObject var viewModel: PhotoGridViewModel
    @State private var draggingItem: PhotoItem?
    /// The folder tile currently under a move-capable drag, highlighted so the
    /// user can see where the drop will land (Windows draws a highlight adorner).
    @State private var highlightedFolderID: PhotoItem.ID?
    @StateObject private var autoScroller = DragAutoScroller()

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
                            PhotoItemView(
                                photo: photo,
                                draggingItem: $draggingItem,
                                isDropTarget: highlightedFolderID == photo.id
                            )
                            .id(photo.id)
                            .contextMenu {
                                PhotoContextMenu(photo: photo)
                            }
                            .onDrag {
                                // Every tile except ".." is a drag source now:
                                // photos reorder or move, folders move. The whole
                                // selection travels when the dragged item is in it.
                                guard photo.kind != .parent else { return NSItemProvider() }
                                draggingItem = photo
                                return NSItemProvider(photoItemIDs: viewModel.dragSet(startingFrom: photo))
                            }
                            .onDrop(of: [.photoItem, .fileURL], delegate: GridDropDelegate(
                                target: photo,
                                viewModel: viewModel,
                                items: $viewModel.photos,
                                draggingItem: $draggingItem,
                                highlightedFolderID: $highlightedFolderID,
                                autoScroller: autoScroller
                            ))
                        }
                    }
                    .padding(Self.outerPadding)
                    .background(ScrollViewIntrospector { autoScroller.scrollView = $0 })
                }
                // Background drop target: a drop that misses every cell (empty
                // space below the grid, or the gaps) imports at the end / drops a
                // reorder at the end.
                .onDrop(of: [.photoItem, .fileURL], delegate: GridDropDelegate(
                    target: nil,
                    viewModel: viewModel,
                    items: $viewModel.photos,
                    draggingItem: $draggingItem,
                    highlightedFolderID: $highlightedFolderID,
                    autoScroller: autoScroller
                ))
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

/// One kind-aware drop delegate for both photo and folder tiles (and, with a nil
/// `target`, the grid background). It decides by target kind and payload type:
///
///   payload is fileURLs   → import into the current folder at this index
///   target is folder / .. → move the dragged items into it
///   target is a photo     → reorder (live-move, as slice 03)
///
/// The live reorder animation still rides on `draggingItem` (@State), which we
/// have because that drag started inside the app; the payload's ids matter for
/// external drops, where there is no `draggingItem` to consult.
struct GridDropDelegate: DropDelegate {
    let target: PhotoItem?
    let viewModel: PhotoGridViewModel
    @Binding var items: [PhotoItem]
    @Binding var draggingItem: PhotoItem?
    @Binding var highlightedFolderID: PhotoItem.ID?
    let autoScroller: DragAutoScroller

    private var targetIsFolder: Bool { target?.isFolder == true }

    func validateDrop(info: DropInfo) -> Bool {
        info.hasItemsConforming(to: [.fileURL, .photoItem])
    }

    func dropUpdated(info: DropInfo) -> DropProposal? {
        if info.hasItemsConforming(to: [.fileURL]) {
            return DropProposal(operation: .copy)   // import = copy in
        }
        return DropProposal(operation: .move)
    }

    func dropEntered(info: DropInfo) {
        autoScroller.start()

        // External file drops get no live reorder or highlight — they import.
        guard info.hasItemsConforming(to: [.photoItem]), let dragging = draggingItem else { return }

        if let target, target.isFolder {
            // A move-capable hover over a folder tile: highlight it (unless it is
            // the very folder being dragged).
            if target.id != dragging.id { highlightedFolderID = target.id }
            return
        }

        // Reorder: live-move the dragged photo to the hovered photo's slot, the
        // same animation slice 03 used. Folders never reorder.
        guard let target, !target.isFolder, !dragging.isFolder, target.id != dragging.id else { return }
        guard let from = items.firstIndex(of: dragging),
              let to = items.firstIndex(of: target) else { return }
        withAnimation(.default) {
            items.move(fromOffsets: IndexSet(integer: from), toOffset: to > from ? to + 1 : to)
        }
    }

    func dropExited(info: DropInfo) {
        if highlightedFolderID == target?.id { highlightedFolderID = nil }
    }

    func performDrop(info: DropInfo) -> Bool {
        autoScroller.stop()
        highlightedFolderID = nil

        // Import: load the file URLs and hand them to the view model.
        if info.hasItemsConforming(to: [.fileURL]) {
            let providers = info.itemProviders(for: [.fileURL])
            let insertIndex = importInsertIndex()
            let vm = viewModel
            Self.loadURLs(from: providers) { urls in
                guard !urls.isEmpty else { return }
                Task { @MainActor in
                    await vm.importPhotos(urls, atImageIndex: insertIndex)
                }
            }
            return true
        }

        // Internal drag.
        let dragged = draggingItem
        draggingItem = nil
        guard let dragged else { return false }

        if let target, target.isFolder {
            let vm = viewModel
            let destination = target.filePath
            let name = target.fileName
            Task { @MainActor in
                let ids = vm.dragSet(startingFrom: dragged)
                await vm.moveItems(ids, into: destination, targetName: name)
            }
            return true
        }

        // A reorder onto the background lands the dragged photo at the end; onto a
        // photo, dropEntered already positioned it. Either way, commit the order.
        if target == nil, !dragged.isFolder, let from = items.firstIndex(of: dragged) {
            let last = items.count - 1
            items.move(fromOffsets: IndexSet(integer: from), toOffset: last + 1)
        }
        viewModel.updateDisplayOrder()
        return true
    }

    /// Where an import lands, as an image-only index. On a cell, that's the count
    /// of image tiles before it; on the background (or a folder), the end.
    private func importInsertIndex() -> Int {
        let imageCount = items.filter { !$0.isFolder }.count
        guard let target, let gridIndex = items.firstIndex(of: target) else { return imageCount }
        return items.prefix(gridIndex).filter { !$0.isFolder }.count
    }

    /// Load file URLs from drop providers off the main thread, then deliver them
    /// on the main thread. Handles both the `Data` (bookmark/URL data) and direct
    /// `URL` item shapes AppKit may hand back.
    private static func loadURLs(from providers: [NSItemProvider], completion: @escaping ([URL]) -> Void) {
        let group = DispatchGroup()
        let lock = NSLock()
        var collected: [(offset: Int, url: URL)] = []

        for (offset, provider) in providers.enumerated() {
            group.enter()
            provider.loadItem(forTypeIdentifier: UTType.fileURL.identifier, options: nil) { item, _ in
                var url: URL?
                if let data = item as? Data {
                    url = URL(dataRepresentation: data, relativeTo: nil)
                } else if let direct = item as? URL {
                    url = direct
                }
                if let url {
                    lock.lock()
                    collected.append((offset, url))
                    lock.unlock()
                }
                group.leave()
            }
        }

        group.notify(queue: .main) {
            // Preserve the drop order regardless of which provider finished first.
            completion(collected.sorted { $0.offset < $1.offset }.map { $0.url })
        }
    }
}

/// Auto-scrolls the grid while a drag hovers near its top or bottom edge. Ports
/// the Windows numbers exactly: within 50 px of an edge, up to 10 px per tick at
/// 50 FPS, ramping with proximity. Started on `dropEntered`; stops itself the
/// moment the mouse button is released, so a cancelled drag (which fires no
/// `performDrop`) still leaves the timer torn down.
@MainActor
final class DragAutoScroller: ObservableObject {
    weak var scrollView: NSScrollView?

    private var timer: Timer?
    private static let margin: CGFloat = 50
    private static let maxSpeed: CGFloat = 10

    func start() {
        guard timer == nil else { return }
        // The timer must run in `.common` modes or it would be starved during the
        // drag's event-tracking run loop.
        let t = Timer(timeInterval: 0.02, repeats: true) { [weak self] _ in
            Task { @MainActor in self?.tick() }
        }
        RunLoop.main.add(t, forMode: .common)
        timer = t
    }

    func stop() {
        timer?.invalidate()
        timer = nil
    }

    private func tick() {
        // No button down means the drag is over (dropped or cancelled): stop.
        guard NSEvent.pressedMouseButtons != 0 else { stop(); return }
        guard let scrollView, let window = scrollView.window else { return }

        let screenPoint = NSEvent.mouseLocation
        let windowPoint = window.convertPoint(fromScreen: screenPoint)
        let frame = scrollView.convert(scrollView.bounds, to: nil)   // window coords
        guard frame.contains(windowPoint) else { return }

        // AppKit window coords: y grows upward, so the viewport top is maxY.
        let distanceFromTop = frame.maxY - windowPoint.y
        let distanceFromBottom = windowPoint.y - frame.minY

        var delta: CGFloat = 0
        if distanceFromTop >= 0, distanceFromTop < Self.margin {
            delta = -Self.maxSpeed * (1 - distanceFromTop / Self.margin)      // scroll up
        } else if distanceFromBottom >= 0, distanceFromBottom < Self.margin {
            delta = Self.maxSpeed * (1 - distanceFromBottom / Self.margin)    // scroll down
        }
        guard delta != 0 else { return }
        scroll(scrollView, by: delta)
    }

    /// SwiftUI's ScrollView uses a flipped document view, so a larger clip-bounds
    /// origin y reveals later content; `delta > 0` scrolls down.
    private func scroll(_ scrollView: NSScrollView, by delta: CGFloat) {
        let clip = scrollView.contentView
        let documentHeight = scrollView.documentView?.frame.height ?? 0
        let maxOriginY = max(0, documentHeight - clip.bounds.height)
        var origin = clip.bounds.origin
        origin.y = min(max(0, origin.y + delta), maxOriginY)
        clip.setBoundsOrigin(origin)
        scrollView.reflectScrolledClipView(clip)
    }
}

/// Finds the enclosing `NSScrollView` for a SwiftUI `ScrollView` by walking up
/// from a zero-size probe placed in its content, then hands it back once. Used
/// only so the auto-scroller can move the viewport during a drag — SwiftUI
/// exposes no way to set the scroll offset in pixels.
struct ScrollViewIntrospector: NSViewRepresentable {
    let onFound: (NSScrollView) -> Void

    func makeNSView(context: Context) -> NSView {
        let view = NSView(frame: .zero)
        DispatchQueue.main.async {
            var ancestor = view.superview
            while let current = ancestor {
                if let scrollView = current as? NSScrollView {
                    onFound(scrollView)
                    break
                }
                ancestor = current.superview
            }
        }
        return view
    }

    func updateNSView(_ nsView: NSView, context: Context) {}
}

struct PhotoItemView: View {
    @EnvironmentObject var viewModel: PhotoGridViewModel
    let photo: PhotoItem
    @Binding var draggingItem: PhotoItem?
    /// True while a move-capable drag hovers this folder tile.
    var isDropTarget: Bool = false

    var body: some View {
        VStack(spacing: 4) {
            if let thumbnail = photo.thumbnail {
                Image(nsImage: thumbnail)
                    .resizable()
                    .aspectRatio(contentMode: .fit)
                    .frame(width: viewModel.thumbnailSize, height: viewModel.thumbnailSize)
                    .cornerRadius(8)
                    // A play badge marks video tiles at a glance — Windows relies on
                    // the frame alone, but the badge is cheap and clearer.
                    .overlay(alignment: .center) {
                        if photo.isVideo { PlayBadge() }
                    }
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
                .fill(backgroundFill)
        )
        .overlay(
            RoundedRectangle(cornerRadius: 8)
                .stroke(strokeColor, lineWidth: isDropTarget ? 3 : 2)
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

    /// A highlighted folder drop target wins over the selection tint.
    private var backgroundFill: Color {
        if isDropTarget { return Color.accentColor.opacity(0.35) }
        return viewModel.selectedPhotos.contains(photo.id) ? Color.accentColor.opacity(0.2) : Color.clear
    }

    private var strokeColor: Color {
        if isDropTarget { return Color.accentColor }
        return viewModel.selectedPhotos.contains(photo.id) ? Color.accentColor : Color.clear
    }
}

/// A small semi-transparent play triangle centred on a video tile's thumbnail so
/// videos are distinguishable from photos without opening them.
struct PlayBadge: View {
    var body: some View {
        Image(systemName: "play.fill")
            .font(.system(size: 18))
            .foregroundColor(.white)
            .padding(12)
            .background(Circle().fill(Color.black.opacity(0.5)))
    }
}

/// The right-click menu, built from the item's `kind`. Each case lists only the
/// entries in the slice-04 table; later slices drop their items at the marked
/// insertion points, keeping this a one-line change for them.
struct PhotoContextMenu: View {
    @EnvironmentObject var viewModel: PhotoGridViewModel
    @Environment(\.openWindow) private var openWindow
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
            exportMenu
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
            compareButton
            exportMenu
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

    /// Image Compare — enabled only when exactly two non-folder photos are
    /// selected, the Windows `CanCompareImages` rule. Opens (or brings forward)
    /// the data-driven compare window for the ordered pair.
    private var compareButton: some View {
        Button("Image Compare") {
            if let pair = viewModel.comparePair {
                openWindow(id: "compare", value: pair)
            }
        }
        .disabled(!viewModel.canCompare)
    }

    /// Export ▸ WebP / PNG / JPG. Shown for images and folders (a folder exports
    /// the images it contains), never for videos — matching the Windows menu.
    /// Windows also lists PhotoAI upscale here; that is deferred, so there is no
    /// separator and just the three format entries.
    private var exportMenu: some View {
        Menu("Export") {
            ForEach(ExportFormat.allCases, id: \.self) { format in
                Button(format.folderName) {
                    viewModel.exportSelection(photo, as: format)
                }
            }
        }
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
