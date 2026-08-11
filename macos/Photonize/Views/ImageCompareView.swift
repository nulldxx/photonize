import SwiftUI

/// The A/B compare window content: image B underneath, image A masked to the
/// pointer's X, a white drop-shadowed divider on hover, filename labels in the
/// bottom corners. The SwiftUI port of Windows' `CompareWindow` +
/// `ImageCompareControl`.
struct ImageCompareView: View {
    let pair: ComparePair

    @Environment(\.dismiss) private var dismiss

    @State private var imageA: NSImage?
    @State private var imageB: NSImage?
    /// The divider's X in the comparison view's coordinate space. Drives the mask
    /// on image A and the divider line's position.
    @State private var dividerX: CGFloat = 0
    /// True while the pointer is over the image, so the divider is shown; fades on
    /// exit, matching WPF's `MouseEnter`/`MouseLeave` toggle.
    @State private var isHovering = false
    /// The first layout centres the divider; later layouts (resize) only clamp it,
    /// so a resize does not yank the divider back to the middle.
    @State private var hasCentered = false

    /// Longest-edge decode size. Comparing two 60 MP files at full resolution
    /// gains nothing visible over the window's backing store and costs half a
    /// gigabyte, so both are downsampled to roughly a Retina 1200×800 window.
    private static let maxLoadDimension = 2560

    private var isLoaded: Bool { imageA != nil && imageB != nil }

    var body: some View {
        VStack(spacing: 0) {
            header
            Divider()
            comparison
            Divider()
            statusBar
        }
        .frame(minWidth: 600, minHeight: 400)
        .task {
            // Decode both off the main actor, orientation-aware (slice 02 gave the
            // thumbnail path the EXIF transform). Held as NSImages, not URLs, so
            // the window never outlives the main window's security scope.
            let generator = ThumbnailGenerator()
            async let a = generator.generateThumbnail(for: pair.urlA, maxSize: Self.maxLoadDimension)
            async let b = generator.generateThumbnail(for: pair.urlB, maxSize: Self.maxLoadDimension)
            let (loadedA, loadedB) = await (a, b)
            imageA = loadedA
            imageB = loadedB
        }
    }

    // MARK: - Header

    private var header: some View {
        HStack {
            Text("Image Comparison — Hover to compare")
                .font(.system(size: 15, weight: .semibold))
            Spacer()
            Button("✕ Close") { dismiss() }
                .keyboardShortcut(.cancelAction)   // Esc closes, matching WPF
        }
        .padding(.horizontal, 15)
        .padding(.vertical, 10)
        .background(.bar)
    }

    // MARK: - Comparison

    private var comparison: some View {
        GeometryReader { geo in
            ZStack {
                Color.black

                if isLoaded {
                    // Both images fit into the same frame so the mask lines up.
                    // Mismatched aspect ratios letterbox into the shared frame
                    // rather than stretching either — a like-for-like comparison
                    // of an edited and unedited shot must not distort one.
                    if let imageB {
                        Image(nsImage: imageB)
                            .resizable()
                            .scaledToFit()
                            .frame(width: geo.size.width, height: geo.size.height)
                    }
                    if let imageA {
                        Image(nsImage: imageA)
                            .resizable()
                            .scaledToFit()
                            .frame(width: geo.size.width, height: geo.size.height)
                            .mask(alignment: .leading) {
                                Rectangle().frame(width: dividerX)
                            }
                    }

                    divider(height: geo.size.height)
                    labels
                } else {
                    ProgressView()
                }
            }
            .contentShape(Rectangle())
            .onContinuousHover { phase in
                switch phase {
                case .active(let location):
                    isHovering = true
                    dividerX = min(max(location.x, 0), geo.size.width)
                case .ended:
                    isHovering = false
                }
            }
            .onAppear {
                if !hasCentered {
                    dividerX = geo.size.width / 2
                    hasCentered = true
                }
            }
            .onChange(of: geo.size) { newSize in
                dividerX = min(dividerX, newSize.width)
            }
        }
    }

    /// The 2 pt white divider with a drop shadow, shown on hover. Kept in the
    /// hierarchy and faded via opacity so it eases in and out rather than popping.
    private func divider(height: CGFloat) -> some View {
        Rectangle()
            .fill(Color.white)
            .frame(width: 2, height: height)
            .shadow(color: .black.opacity(0.8), radius: 4)
            .position(x: dividerX, y: height / 2)
            .opacity(isHovering ? 1 : 0)
            .animation(.easeInOut(duration: 0.15), value: isHovering)
            .allowsHitTesting(false)
    }

    /// Filename labels: A bottom-left, B bottom-right, over a translucent capsule
    /// so they stay legible on any image and in either appearance.
    private var labels: some View {
        VStack {
            Spacer()
            HStack {
                label(pair.urlA.lastPathComponent)
                Spacer()
                label(pair.urlB.lastPathComponent)
            }
        }
        .padding(10)
        .allowsHitTesting(false)
    }

    private func label(_ text: String) -> some View {
        Text(text)
            .font(.system(size: 12, weight: .semibold))
            .foregroundColor(.white)
            .padding(.horizontal, 8)
            .padding(.vertical, 4)
            .background(Capsule().fill(Color.black.opacity(0.6)))
    }

    // MARK: - Status bar

    private var statusBar: some View {
        HStack {
            Text(statusText)
                .font(.system(size: 12))
                .foregroundColor(.secondary)
            Spacer()
        }
        .padding(.horizontal, 15)
        .padding(.vertical, 6)
        .background(.bar)
    }

    private var statusText: String {
        isLoaded
            ? "Comparing: \(pair.urlA.lastPathComponent) (left) vs \(pair.urlB.lastPathComponent) (right)"
            : "Loading images…"
    }
}
