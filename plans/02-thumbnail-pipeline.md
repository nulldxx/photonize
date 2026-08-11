# Slice 02 — Thumbnail pipeline

**Depends on:** 01. **Blocks:** 03, 04, 05, 08, 10.

## Context

Opening a large folder on macOS shows an empty grid until every thumbnail has been decoded, one
at a time, on a single background queue.

`PhotoGridViewModel.loadPhotos()` (`macos/Photonize/ViewModels/PhotoGridViewModel.swift:106-117`):

```swift
for imageFile in imageFiles {
    let thumbnail = await thumbnailGenerator.generateThumbnail(for: imageFile, maxSize: Int(thumbnailSize))
    newPhotos.append(PhotoItem(…, thumbnail: thumbnail))
}
photos = newPhotos    // ← line 119, the grid populates only here
```

Every `await` is sequential, and `photos` is not assigned until the loop finishes. There is no
cancellation, so switching folders mid-load leaves the old load running and racing the new one.

The decoding itself is also wasteful. `ThumbnailGenerator.generateThumbnail`
(`macos/Photonize/Services/ThumbnailGenerator.swift:16-27`) calls `NSImage(contentsOf:)`, which
decodes the image at **full resolution** into memory, then resizes it with `lockFocus`/`draw`.
For a folder of 40 MP raws that is gigabytes of transient allocation. It also ignores EXIF
orientation, so portrait photos from a phone appear sideways.

Windows solved all of this in `Photonize/Services/WorkerQueue.cs` (dedicated threads over a
`BlockingCollection`, worker count = `Environment.ProcessorCount`, staggered starts, completion
and failure events, cancellation tokens) plus `BitmapImage.DecodePixelWidth` so the decoder never
materialises the full image.

**Outcome:** the grid appears instantly with placeholders and fills in as thumbnails arrive;
switching folders cancels the previous load; memory use is bounded; orientation is respected.
Everything later in the roadmap that processes files in bulk — export (05), video thumbnails (08)
— runs on the loader built here.

---

## Design

### Progressive population

Split `loadPhotos` into two phases, mirroring what Windows does:

1. **Enumerate and publish.** Build the whole `[PhotoItem]` — `..`, folders, then image files,
   each with `thumbnail: nil` — and assign `photos` immediately. The grid renders at once with
   the existing placeholder cell (`PhotoGridView.swift:91-101` already draws a grey rectangle with
   a `photo` SF Symbol when `thumbnail == nil`).
2. **Fill.** Enqueue one thumbnail job per image; each completion writes back into
   `photos[index].thumbnail` on the main actor.

Write back by **`id`, not index** (`photos.firstIndex(where: { $0.id == id })`) — slices 06 and 10
reorder the array while loads may still be in flight.

`PhotoItem` is a struct in an `@Published` array, so per-item mutation republishes the whole
array. With `LazyVGrid` and `Identifiable` items SwiftUI diffs this acceptably, but coalesce
writes: buffer completions and flush on a ~50 ms timer rather than republishing per photo.

### Bounded-concurrency loader

New `macos/Photonize/Services/ThumbnailLoader.swift` — the Swift analogue of `WorkerQueue<T>`:

```swift
actor ThumbnailLoader {
    init(maxConcurrent: Int = ProcessInfo.processInfo.activeProcessorCount)

    /// Cancels everything in flight and returns a fresh generation token.
    func cancelAll()

    /// Enqueue one thumbnail request. Results arrive on the callback, main-actor.
    func request(_ url: URL, size: Int, generation: Int) async -> NSImage?
}
```

Implementation: `withTaskGroup` fed from a queue, capped at `maxConcurrent` in-flight children —
the direct equivalent of the Windows worker count. Windows' 3-second stagger delay exists only to
keep `tpai.exe` process launches from stampeding; thumbnail decoding does not need it, so leave it
out.

Cancellation is by **generation counter**. `loadPhotos` increments a counter, and every enqueued
job carries it; results from a stale generation are discarded. This is simpler and less
error-prone than threading a `CancellationToken` equivalent through, and it handles the real case
(user clicks through five folders quickly) exactly.

### Decode properly

Replace the body of `generateThumbnail` with ImageIO:

```swift
let opts: [CFString: Any] = [
    kCGImageSourceCreateThumbnailFromImageAlways: true,
    kCGImageSourceThumbnailMaxPixelSize: maxSize,
    kCGImageSourceCreateThumbnailWithTransform: true,   // honours EXIF orientation
    kCGImageSourceShouldCacheImmediately: true,
]
guard let src = CGImageSourceCreateWithURL(url as CFURL, nil),
      let cg = CGImageSourceCreateThumbnailAtIndex(src, 0, opts as CFDictionary)
else { return nil }
return NSImage(cgImage: cg, size: .zero)
```

This never decodes the full image, uses any embedded JPEG thumbnail when one is big enough, and
fixes orientation. `generatePreview` keeps `NSImage(contentsOf:)` — the preview pane genuinely
wants full resolution — but should also apply orientation.

### Cache and resize behaviour

- `NSCache<NSString, NSImage>` keyed by `path|mtime|requestedSize`. The mtime component means an
  edited file re-renders; the size component means the slider's size buckets each get their own
  entry. Cap with `countLimit` (a few hundred) and let `NSCache` evict under pressure.
- **Fix the blurry-slider bug.** Thumbnails are currently baked at whatever `thumbnailSize` was
  when the folder loaded, but displayed at the live slider value
  (`PhotoGridView.swift:89`), so enlarging upscales a small bitmap until you hit Refresh. Debounce
  the slider (~300 ms) and re-request any thumbnail whose cached size is smaller than the new
  display size. Shrinking needs no re-request.

### Folder icons

Delete the 40 lines of hand-drawn `NSBezierPath` folder art
(`ThumbnailGenerator.swift:36-76`) and use `NSWorkspace.shared.icon(forFile: url.path)` with
`.size` set. It is one line, it matches the system appearance in both light and dark mode, and it
picks up custom folder icons — which the drawn version cannot. Cache the generic folder icon once
rather than asking per folder, except where a folder has a custom icon.

---

## Files

**New**
- `macos/Photonize/Services/ThumbnailLoader.swift` — bounded-concurrency loader with generation
  based cancellation

**Modified**
- `macos/Photonize/Services/ThumbnailGenerator.swift` — ImageIO thumbnail decode, orientation-aware
  preview, `NSCache`, `NSWorkspace` folder icons; drop `resizeImage` and the bezier-path drawing
- `macos/Photonize/ViewModels/PhotoGridViewModel.swift` — two-phase `loadPhotos`, generation
  counter, coalesced write-back, debounced re-request on slider growth
- `macos/Photonize/Views/PhotoGridView.swift` — placeholder cell gets a subtle progress treatment
  so a slow thumbnail reads as loading rather than broken

---

## Tests

These land properly in slice 12 (which creates the test target), but write them now and keep them
alongside the code until there is somewhere to put them:

- `ThumbnailLoader` never exceeds `maxConcurrent` in flight (instrument with an atomic counter).
- `cancelAll()` followed by a new generation: results from the old generation are discarded.
- Cache key changes when the file's mtime changes.
- `generateThumbnail` on a fixture with EXIF orientation 6 returns a portrait-shaped image.

## Verification

1. **Progressive load.** Open a folder of 500+ JPEGs. The grid must appear within a beat, showing
   placeholders that fill in. Before this slice the window is empty for the whole load.
2. **Memory.** Run the same folder under Instruments' Allocations. Peak footprint should be a
   small multiple of `maxConcurrent × thumbnail size`, not proportional to the folder's pixel
   count. Compare against a pre-slice build.
3. **Cancellation.** Click rapidly through six folders. The final folder's contents are correct,
   with no thumbnails from earlier folders leaking in, and CPU settles promptly.
4. **Orientation.** Put a phone photo with EXIF orientation 6 in a folder. It appears upright in
   both grid and preview.
5. **Slider.** Load at 100 px, drag to 400 px. Thumbnails re-render sharp within a moment; no
   Refresh needed.
6. **Folder icons.** Folders show the system folder icon. Give one a custom icon in Finder's
   Get Info and confirm it appears.

---

## Out of scope

- Disk-backed thumbnail cache (`macos/SETUP.md:185` lists it as a future enhancement). The
  in-memory `NSCache` plus ImageIO's embedded-thumbnail path is enough; revisit only if profiling
  says otherwise.
- Video thumbnails — slice 08 adds `AVAssetImageGenerator` as another job type on this same loader.
- `QLThumbnailGenerator`. It handles more file types but is slower for plain images and returns
  QuickLook-styled results with borders, which is not what the grid wants.
