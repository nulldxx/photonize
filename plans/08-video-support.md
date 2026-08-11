# Slice 08 — Video support

**Depends on:** 02, 03, 04. **Blocks:** nothing.

## Context

Windows 0.1.23 added MP4/MOV/M4V alongside photos: video thumbnails with correct aspect ratio,
playback in the preview pane with timeline scrubbing, play/pause and volume, and a video-specific
context menu without the image-only entries.

Getting there on Windows was expensive. `Photonize/App.xaml.cs` probes **five** candidate paths
for an FFmpeg binary at startup and, failing that, shows a warning dialog offering to open
ffmpeg.org in a browser (`76a54cd`). `Photonize.Installer/Product.wxs` had to grow entries for
FFMpegCore and InstancesCore (`0febdf3`). `ThumbnailGenerator.cs` calls `FFProbe.AnalyseAsync` for
duration and dimensions, then grabs a frame.

**None of that applies here.** AVFoundation is a system framework on every supported Mac and
decodes MP4, MOV and M4V natively. No dependency, no installer entries, no startup probe, no
warning dialog. This slice is materially cheaper than its Windows counterpart, and worth doing for
that reason alone.

macOS today filters files through `ThumbnailGenerator.isImageFile`
(`macos/Photonize/Services/ThumbnailGenerator.swift:8-12`), whose whitelist has no video
extensions, so video files are invisible — they are neither shown nor renamed, which means a
folder with mixed content renumbers only the photos and silently leaves the clips behind.

**Outcome:** videos appear in the grid with real thumbnails, play in the preview pane with the
Windows control set, and are included in batch renames.

---

## Design

### Recognising videos

- `ThumbnailGenerator`: add `isVideoFile(_:)` for `mp4`, `mov`, `m4v` and a combined
  `isMediaFile(_:)`. `loadPhotos` switches to `isMediaFile` so videos enumerate alongside images.
- `PhotoItem`: add `var isVideo: Bool` and extend the `PhotoKind` enum from slice 04 with `.video`
  (slice 04 already declares the case; this slice makes it reachable).
- Videos participate in reordering and renaming exactly like photos — that is the whole point.
  `FileRenamer` is extension-agnostic already.

**One deliberate divergence from Windows to consider:** Windows recognises `.mov` but macOS users
also shoot `.mp4`/`.mov` from iPhone in HEVC, and many have `.avi` or `.mkv` lying around. Stick to
the Windows three for parity. Adding more is a one-line change later, but a `.mkv` that AVFoundation
cannot decode would show a broken tile, so do not widen the list speculatively.

### Thumbnails

Another job type on the slice-02 loader:

```swift
let asset = AVURLAsset(url: url)
let gen = AVAssetImageGenerator(asset: asset)
gen.appliesPreferredTrackTransform = true      // honours rotation metadata
gen.maximumSize = CGSize(width: maxSize, height: maxSize)
let time = duration < 2 ? CMTime(seconds: 0.1, …) : CMTime(seconds: 1, …)
```

Matching Windows: frame at 1 s, or 100 ms for clips shorter than that. `appliesPreferredTrackTransform`
is the AVFoundation equivalent of the aspect/rotation fix Windows needed (`64ec696` fixed video
thumbnails being stretched square) — without it, portrait iPhone video comes out sideways.

Use the async `image(at:)` API where available and fall back to `copyCGImage` on macOS 13.

Failures get a drawn "broken video" placeholder, as Windows does. Overlay a small play-triangle
badge on every video tile so videos are distinguishable at a glance — Windows relies on the
thumbnail alone, but a badge is cheap and clearer.

Cache key must include the timestamp used, so the slice-02 `path|mtime|size` key gains a kind
component.

### Playback

Extend the preview pane. When the selected item is a video, show an `AVPlayer` surface plus the
Windows control set (`Photonize/MainWindow.xaml`, video sub-UI):

| Control | Behaviour |
|---|---|
| Play/pause | ▶ / ⏸, toggles; Windows binds this via `BoolToPlayPauseIconConverter` |
| Timeline | Scrubbable slider; seek on release, not continuously |
| Time label | `mm:ss / mm:ss` |
| Volume | 0–1 slider, **default 0.8** (Windows' default) |

Behaviours to match: **auto-play on selection**, and **reset to the start on end** rather than
looping (Windows `VideoPlayer_MediaEnded`).

Use `AVPlayer` with a `VideoPlayer` (AVKit) surface but **custom controls**, not AVKit's default
overlay — the default chrome will not match the Windows layout and cannot be reduced to this
control set.

Implementation notes:
- Drive the time label and slider from `addPeriodicTimeObserver` (~10 Hz), not a `Timer`.
- Suspend the observer's slider updates while the user is dragging, or the thumb fights the
  playhead. Windows handles this with explicit press/release handlers
  (`VideoTimeline_PreviewMouseDown`/`Up`).
- Pause and tear down the player when the selection changes or the pane closes; a live `AVPlayer`
  on a deselected item keeps audio playing and holds the file open.
- A decode failure shows an inline message in the pane, not a modal alert. Windows shows a
  "Video Playback Error" message box; inline is less obnoxious and equally clear.

### Context menu

The `.video` case from slice 04 gets: Show in Finder · Tags ▸ (slice 10) · Delete. **No Export**
and **no Image Compare** — Windows omits both deliberately, and the image exporter cannot read a
video anyway.

Also exclude videos from the export selection when a mixed selection is exported: export only the
images and say so in the completion message, rather than reporting failures for every clip.

---

## Files

**Modified**
- `macos/Photonize/Services/ThumbnailGenerator.swift` — `isVideoFile`, `isMediaFile`,
  `AVAssetImageGenerator` thumbnails, broken-video placeholder, kind in the cache key
- `macos/Photonize/Models/PhotoItem.swift` — `isVideo`, `.video` kind
- `macos/Photonize/ViewModels/PhotoGridViewModel.swift` — enumerate media not just images; player
  state (playing, position, duration, volume); teardown on selection change
- `macos/Photonize/Views/PreviewPane.swift` — video surface and transport controls
- `macos/Photonize/Views/PhotoGridView.swift` — play badge on video tiles; `.video` context menu
- `macos/Photonize/Services/ImageExporter.swift` — skip videos in mixed selections
- `macos/project.yml` — link AVFoundation and AVKit
- `macos/README.md`, `macos/SETUP.md` — video support, and the note that no FFmpeg is required

---

## Tests

- `isVideoFile` accepts `.mp4`, `.MOV`, `.m4v` (case-insensitive) and rejects `.jpg`, `.mkv`.
- Mixed-folder enumeration returns images and videos in one filename-sorted sequence.
- The thumbnail timestamp is 0.1 s for a 1-second fixture and 1 s for a 30-second one.
- Exporting a mixed selection produces outputs only for the images and reports the video count as
  skipped, not failed.

## Verification

1. **No FFmpeg.** On a Mac with no FFmpeg installed anywhere, videos still get thumbnails. This is
   the headline difference from Windows.
2. **Thumbnails.** A folder of MP4s shows real frames with correct aspect ratio. Add a
   portrait-orientation iPhone clip — it appears upright, not sideways.
3. **Rename includes videos.** A folder with 5 photos and 3 videos, prefix `holiday` → all 8 files
   renumbered `holiday-00001` … `holiday-00008` in display order, extensions preserved.
4. **Playback.** Select a video — it starts playing automatically. Play/pause toggles and the icon
   follows. The timeline advances and the label reads `mm:ss / mm:ss`.
5. **Scrubbing.** Drag the timeline to 75% — playback resumes from there and the thumb does not
   jump back under the playhead while dragging.
6. **Volume.** Default is 0.8. Dragging to 0 mutes.
7. **End of clip.** Let one play to the end — it stops and resets to the start, and does not loop.
8. **Selection change.** Select a video, then another photo — audio stops immediately. Close the
   preview pane mid-playback — audio stops.
9. **Bad file.** Rename a text file to `.mp4` — the tile shows the broken placeholder and selecting
   it shows an inline error rather than crashing or opening a modal.
10. **Context menu.** Right-click a video — no Export, no Image Compare.
11. **Mixed export.** Select 3 photos and 2 videos → Export ▸ WebP. Three files exported, and the
    message says two videos were skipped.

---

## Out of scope

- Video export or transcoding. Windows does not do it either.
- Video comparison in the compare window (slice 07 is images only).
- Extra container formats (`.avi`, `.mkv`, `.webm`) — matching Windows' three keeps the clients
  interchangeable and avoids tiles for files AVFoundation cannot decode.
- Frame-accurate stepping, trimming, or thumbnail scrubbing on hover.
