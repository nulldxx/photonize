import SwiftUI
import AVKit
import AVFoundation

struct PreviewPane: View {
    @EnvironmentObject var viewModel: PhotoGridViewModel
    @State private var previewImage: NSImage?
    @StateObject private var video = VideoPlaybackController()

    var body: some View {
        VStack(spacing: 0) {
            if let photo = viewModel.previewPhoto, !photo.isFolder {
                if photo.isVideo {
                    videoPreview(photo)
                } else {
                    imagePreview(photo)
                }

                Divider()
                infoPanel(photo)
            } else {
                emptyState
            }
        }
        .onChange(of: viewModel.previewPhoto) { newPhoto in
            configure(for: newPhoto)
        }
        .onAppear {
            configure(for: viewModel.previewPhoto)
        }
        .onDisappear {
            // Closing the pane (or leaving the view) must stop audio and release
            // the file, not leave a live player running on a hidden surface.
            video.teardown()
        }
    }

    // MARK: - Image

    private func imagePreview(_ photo: PhotoItem) -> some View {
        ZStack {
            Color.black

            if let image = previewImage {
                GeometryReader { geometry in
                    Image(nsImage: image)
                        .resizable()
                        .scaledToFit()
                        .frame(maxWidth: geometry.size.width, maxHeight: geometry.size.height)
                        .frame(width: geometry.size.width, height: geometry.size.height)
                }
            } else {
                ProgressView()
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
    }

    // MARK: - Video

    private func videoPreview(_ photo: PhotoItem) -> some View {
        ZStack {
            Color.black

            if video.failed {
                // A decode failure is shown inline, not as a modal alert.
                VStack(spacing: 12) {
                    Image(systemName: "exclamationmark.triangle")
                        .font(.system(size: 40))
                        .foregroundColor(.gray)
                    Text("This video can't be played.")
                        .font(.headline)
                        .foregroundColor(.gray)
                }
            } else {
                VStack(spacing: 0) {
                    VideoSurface(player: video.player)
                        .frame(maxWidth: .infinity, maxHeight: .infinity)
                    VideoTransportControls(video: video)
                        .padding(8)
                        .background(Color(NSColor.controlBackgroundColor))
                }
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
    }

    // MARK: - Shared chrome

    private func infoPanel(_ photo: PhotoItem) -> some View {
        VStack(alignment: .leading, spacing: 8) {
            InfoRow(label: "Name:", value: photo.fileName)
            InfoRow(label: "Type:", value: photo.fileExtension.uppercased())

            if !photo.isVideo, let image = previewImage {
                InfoRow(label: "Size:", value: "\(Int(image.size.width)) × \(Int(image.size.height))")
            }

            if let fileSize = try? FileManager.default.attributesOfItem(atPath: photo.filePath.path)[.size] as? Int64 {
                InfoRow(label: "File Size:", value: formatFileSize(fileSize))
            }
        }
        .padding()
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Color(NSColor.controlBackgroundColor))
    }

    private var emptyState: some View {
        VStack {
            Image(systemName: "photo.on.rectangle")
                .font(.system(size: 60))
                .foregroundColor(.gray)

            Text("No Preview Available")
                .font(.headline)
                .foregroundColor(.gray)
                .padding(.top)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
    }

    // MARK: - Loading

    /// Route the newly selected item to the right surface: an image decodes into
    /// `previewImage`; a video hands off to the player; anything else tears both
    /// down so a deselected clip stops immediately.
    private func configure(for photo: PhotoItem?) {
        guard let photo = photo, !photo.isFolder else {
            previewImage = nil
            video.teardown()
            return
        }

        if photo.isVideo {
            previewImage = nil
            video.load(photo.filePath)
        } else {
            video.teardown()
            loadPreview(for: photo)
        }
    }

    private func loadPreview(for photo: PhotoItem) {
        previewImage = nil
        Task {
            let image = await ThumbnailGenerator().generatePreview(for: photo.filePath)
            await MainActor.run {
                previewImage = image
            }
        }
    }

    private func formatFileSize(_ bytes: Int64) -> String {
        let formatter = ByteCountFormatter()
        formatter.countStyle = .file
        return formatter.string(fromByteCount: bytes)
    }
}

/// The custom transport row — play/pause, a scrubbable timeline, an `mm:ss /
/// mm:ss` label and a volume slider. Matches the Windows control set; AVKit's
/// default chrome is suppressed on the surface below.
struct VideoTransportControls: View {
    @ObservedObject var video: VideoPlaybackController

    var body: some View {
        HStack(spacing: 12) {
            Button(action: { video.togglePlayPause() }) {
                Image(systemName: video.isPlaying ? "pause.fill" : "play.fill")
                    .font(.system(size: 16))
            }
            .buttonStyle(.plain)
            .help(video.isPlaying ? "Pause" : "Play")

            // Seek on release, not continuously: while dragging, the periodic
            // observer's updates are suspended so the thumb doesn't fight the
            // playhead.
            Slider(
                value: $video.currentTime,
                in: 0...max(video.duration, 0.01)
            ) { editing in
                video.isScrubbing = editing
                if !editing { video.seek(to: video.currentTime) }
            }

            Text("\(Self.format(video.currentTime)) / \(Self.format(video.duration))")
                .font(.system(size: 11).monospacedDigit())
                .foregroundColor(.secondary)

            Image(systemName: "speaker.fill")
                .font(.system(size: 11))
                .foregroundColor(.secondary)

            Slider(
                value: Binding(get: { video.volume }, set: { video.setVolume($0) }),
                in: 0...1
            )
            .frame(width: 80)
        }
    }

    static func format(_ seconds: Double) -> String {
        guard seconds.isFinite, seconds >= 0 else { return "00:00" }
        let total = Int(seconds.rounded(.down))
        return String(format: "%02d:%02d", total / 60, total % 60)
    }
}

/// The AVKit playback surface with its default controls suppressed — a bare video
/// view driven entirely by our own transport row.
struct VideoSurface: NSViewRepresentable {
    let player: AVPlayer

    func makeNSView(context: Context) -> AVPlayerView {
        let view = AVPlayerView()
        view.player = player
        view.controlsStyle = .none
        view.videoGravity = .resizeAspect
        return view
    }

    func updateNSView(_ nsView: AVPlayerView, context: Context) {
        if nsView.player !== player {
            nsView.player = player
        }
    }
}

/// Owns the `AVPlayer` for the preview pane and mirrors its state for the custom
/// controls. Playback is auto-started on load and reset to the start (not looped)
/// at the end, matching Windows. `teardown()` is called by the view whenever the
/// selection changes or the pane closes, so a deselected clip never keeps audio
/// playing or holds its file open.
@MainActor
final class VideoPlaybackController: ObservableObject {
    @Published var isPlaying = false
    @Published var currentTime: Double = 0
    @Published var duration: Double = 0
    /// Default 0.8, matching the Windows player, and retained across clips.
    @Published var volume: Double = 0.8
    /// True while the user drags the timeline; suspends the periodic observer's
    /// writes to `currentTime` so the thumb doesn't snap back under the playhead.
    @Published var isScrubbing = false
    @Published var failed = false

    let player = AVPlayer()
    private var timeObserver: Any?
    private var endObserver: NSObjectProtocol?
    private var statusObserver: NSKeyValueObservation?

    func load(_ url: URL) {
        teardown()
        failed = false

        let item = AVPlayerItem(asset: AVURLAsset(url: url))
        player.replaceCurrentItem(with: item)
        player.volume = Float(volume)

        // Surface decode failures inline and pick up the real duration once known.
        statusObserver = item.observe(\.status, options: [.new]) { [weak self] item, _ in
            Task { @MainActor in
                guard let self else { return }
                switch item.status {
                case .failed:
                    self.failed = true
                    self.isPlaying = false
                case .readyToPlay:
                    let d = item.duration.seconds
                    self.duration = d.isFinite ? d : 0
                default:
                    break
                }
            }
        }

        // ~10 Hz, driving the label and slider — no `Timer`.
        let interval = CMTime(seconds: 0.1, preferredTimescale: 600)
        timeObserver = player.addPeriodicTimeObserver(forInterval: interval, queue: .main) { [weak self] time in
            let seconds = time.seconds.isFinite ? time.seconds : 0
            Task { @MainActor in
                guard let self, !self.isScrubbing else { return }
                self.currentTime = seconds
            }
        }

        // Reset to the start on end rather than looping (Windows VideoPlayer_MediaEnded).
        endObserver = NotificationCenter.default.addObserver(
            forName: .AVPlayerItemDidPlayToEndTime, object: item, queue: .main
        ) { [weak self] _ in
            Task { @MainActor in
                guard let self else { return }
                self.player.pause()
                self.player.seek(to: .zero)
                self.currentTime = 0
                self.isPlaying = false
            }
        }

        // Auto-play on selection.
        player.play()
        isPlaying = true
    }

    func togglePlayPause() {
        if isPlaying {
            player.pause()
            isPlaying = false
        } else {
            player.play()
            isPlaying = true
        }
    }

    func seek(to seconds: Double) {
        currentTime = seconds
        player.seek(
            to: CMTime(seconds: seconds, preferredTimescale: 600),
            toleranceBefore: .zero,
            toleranceAfter: .zero
        )
    }

    func setVolume(_ newVolume: Double) {
        volume = newVolume
        player.volume = Float(newVolume)
    }

    func teardown() {
        player.pause()
        if let timeObserver {
            player.removeTimeObserver(timeObserver)
            self.timeObserver = nil
        }
        if let endObserver {
            NotificationCenter.default.removeObserver(endObserver)
            self.endObserver = nil
        }
        statusObserver?.invalidate()
        statusObserver = nil
        player.replaceCurrentItem(with: nil)
        isPlaying = false
        currentTime = 0
        duration = 0
        failed = false
    }
}

struct InfoRow: View {
    let label: String
    let value: String

    var body: some View {
        HStack {
            Text(label)
                .font(.system(size: 11, weight: .semibold))
                .foregroundColor(.secondary)
                .frame(width: 80, alignment: .trailing)

            Text(value)
                .font(.system(size: 11))
                .textSelection(.enabled)
        }
    }
}
