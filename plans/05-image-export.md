# Slice 05 — Image export (WebP / PNG / JPG)

**Depends on:** 01, 02. **Blocks:** 06, 10.

## Context

Export is the most-used Windows feature that macOS lacks entirely. `Services/ImageExporter.cs`
writes WebP, PNG or JPG copies into a subfolder named after the format, with a pre-flight
overwrite prompt, a progress dialog, cancellation, and a refresh afterwards to reveal the new
folder. `Services/WebPExporter.cs` is the older WebP-only path kept for the Explorer
`-e/--export-webp` verb.

Encoder settings to match exactly:

| Format | Windows (ImageSharp) | macOS |
|---|---|---|
| WebP | `WebpEncoder { Quality = 90, FileFormat = Lossy }` | libwebp, quality 90 |
| PNG | `PngEncoder { CompressionLevel = BestCompression }` | ImageIO |
| JPG | `JpegEncoder { Quality = 90 }` | ImageIO, `kCGImageDestinationLossyCompressionQuality = 0.9` |

`macos/SETUP.md:162` claims "macOS doesn't have built-in WebP support". That is half right: ImageIO
has *decoded* WebP since macOS 11, but only gained *encoding* in macOS 14 (Sonoma). Rather than
raise the deployment target off 13 and drop Ventura users, this slice adds the project's first
third-party dependency — **libwebp via SPM** — which is why slice 01's project-file work has to
land first.

This slice also builds the reusable progress sheet. Windows has one `Views/ProgressDialog.xaml`
shared by export, upscale, auto-caption and tag-move; macOS has nothing. Slices 06 and 10 consume
what is built here, so it is designed as a general mechanism, not an export detail.

**Outcome:** right-click → Export ▸ WebP / PNG / JPG on any selection (or a folder) produces a
subfolder of converted images, with progress, cancellation and an overwrite prompt, and the grid
navigates to the result.

---

## Design

### The dependency

Add to `macos/project.yml`:

```yaml
packages:
  libwebp:
    url: https://github.com/SDWebImage/libwebp-Xcode
    from: "1.3.2"
targets:
  Photonize:
    dependencies:
      - package: libwebp
```

`SDWebImage/libwebp-Xcode` is the maintained SPM-packaged build of Google's libwebp; it vendors
the C library with a module map and nothing else — no SDWebImage runtime comes along with it.

Encoding path: `NSImage` → `CGImage` → RGBA8 bitmap via `CGContext` → `WebPEncodeRGBA(ptr, w, h,
stride, 90, &out)` → `Data(bytesNoCopy:)` → write. Free the encoder's buffer with `WebPFree`.
Wrap all of this in one small Swift file so libwebp's C API appears exactly once in the codebase.

### `ImageExporter`

New `macos/Photonize/Services/ImageExporter.swift`, mirroring the C# API shape:

```swift
enum ExportFormat: String, CaseIterable { case webp, png, jpg
    var folderName: String   // "WebP", "PNG", "JPG" — match Windows exactly
    var fileExtension: String
}

enum OverwriteOption { case cancel, overwriteAll, skipExisting }

struct ExportResult { let exported: Int; let skipped: Int; let failed: [String]; let outputURL: URL }

actor ImageExporter {
    func export(_ photos: [PhotoItem],
                in directory: URL,
                as format: ExportFormat,
                overwrite: @MainActor ([String]) async -> OverwriteOption,
                progress: @MainActor (Int, Int, String) -> Void) async throws -> ExportResult
}
```

Order of operations, matching Windows:

1. Create `<directory>/<format.folderName>`.
2. Scan for outputs that already exist; if any, call `overwrite` **once** with the list and honour
   the answer for the whole batch. `.cancel` aborts before any work.
3. Convert with bounded concurrency on the slice-02 loader.
4. Report progress from completions, never from the dispatch loop, so the bar cannot jump
   backwards (Windows learned this in `88ef3e1`).
5. Return counts; the view model sets `directoryPath` to the output folder and reloads
   (Windows `f5adc1f`, `49a28d2`).

When a **folder** is the export target, expand it to its image files first — the Windows
folder-variant menu does this for upscale, and export should behave the same.

### `ProgressSheet` — built here, reused later

New `macos/Photonize/Views/ProgressSheet.swift` plus an observable:

```swift
@MainActor final class OperationProgress: ObservableObject {
    @Published var title: String
    @Published var status: String
    @Published var current: Int
    @Published var total: Int
    @Published private(set) var isCancelling = false
    func cancel()                      // flips isCancelling, sets the Task cancelled
    var task: Task<Void, Never>?
}
```

The sheet shows title, status line, a determinate bar, "N of M", and a Cancel button that becomes
"Cancelling…" once pressed — the same affordances as `Views/ProgressDialog.xaml`. Presented with
`.sheet(item:)` from `ContentView` so it is modal to the window rather than the app.

Cancellation is Swift structured concurrency: `cancel()` cancels the owning `Task`, and the
exporter checks `Task.isCancelled` between files. Partial output stays on disk, as on Windows;
the completion message reports how far it got.

Design this for three callers from the outset:

| Caller | Slice |
|---|---|
| Export to WebP/PNG/JPG | this slice |
| Import from Finder | 06 |
| Move tagged images into sub-folders | 10 |

### Menu wiring

Populate the `Export ▸` insertion point that slice 04 left in `PhotoContextMenu`, for images and
folders (not videos — Windows omits it there deliberately). Three items: WebP, PNG, JPG.

Windows also exposes upscale in this submenu; that is deferred, so the macOS submenu has three
entries and no separator.

---

## Files

**New**
- `macos/Photonize/Services/ImageExporter.swift` — formats, overwrite policy, batch export
- `macos/Photonize/Services/WebPEncoder.swift` — the only file that touches libwebp's C API
- `macos/Photonize/Views/ProgressSheet.swift` — `OperationProgress` + the sheet

**Modified**
- `macos/project.yml` — libwebp package and target dependency
- `macos/Photonize/Views/PhotoGridView.swift` — fill in the `Export ▸` submenu
- `macos/Photonize/ViewModels/PhotoGridViewModel.swift` — `exportSelection(as:)`, progress
  ownership, navigate-to-output on success
- `macos/Photonize/Views/ContentView.swift` — present the progress sheet
- `macos/README.md`, `macos/SETUP.md` — export is no longer "not yet implemented"; correct the
  "macOS doesn't have built-in WebP support" line

---

## Tests

- `ExportFormat.folderName` returns `WebP`/`PNG`/`JPG` — the folder names must match Windows so a
  folder organised on one platform looks the same on the other.
- Round-trip a fixture through `WebPEncoder`: the output decodes via ImageIO, has the source's
  pixel dimensions, and is smaller than the source PNG.
- Overwrite policy: given three existing outputs and `.skipExisting`, exactly the other files are
  written and `skipped == 3`.
- Cancellation mid-batch leaves `exported < total` and reports no failures.

## Verification

1. **All three formats.** Select 20 mixed JPEG/PNG/HEIC photos → Export ▸ WebP. A `WebP` subfolder
   appears with 20 `.webp` files, the grid navigates into it, and Preview.app opens them. Repeat
   for PNG and JPG.
2. **Ventura.** Build and run on a macOS 13 machine (or VM) and export WebP. This is the whole
   reason for the libwebp dependency — if it only works on Sonoma, the dependency is pointless.
3. **Quality parity.** Export the same source image on Windows and macOS; file sizes should be
   within a few percent. A large divergence means the quality setting did not carry across.
4. **Overwrite prompt.** Export twice. The second run prompts once, listing the existing files.
   Choose Skip existing → nothing is rewritten (check mtimes). Choose Overwrite all → all
   rewritten. Choose Cancel → no `WebP` folder changes at all.
5. **Progress and cancel.** Export 500 photos. The bar advances monotonically and the count is
   accurate. Press Cancel around halfway: the sheet says "Cancelling…", the operation stops within
   a second or two, and the summary reports the partial count.
6. **Folder target.** Right-click a subfolder → Export ▸ PNG. Its images are exported, not the
   folder itself.
7. **Orientation.** Export a photo with EXIF orientation 6 and confirm the output is upright —
   this catches a `CGImage` path that skipped the slice-02 transform.

---

## Out of scope

- Topaz PhotoAI upscale in the Export submenu — deferred (see `plans/README.md`).
- Configurable export quality. Windows hardcodes 90; matching it keeps the two clients
  interchangeable. A Settings-scene control could come later.
- Export presets, resizing, or watermarking — neither client has them.
- The `-e/--export-webp` command-line entry point — slice 11, which builds on this exporter.
