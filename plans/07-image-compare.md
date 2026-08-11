# Slice 07 — Image compare

**Depends on:** 03. **Blocks:** nothing.

## Context

Windows' compare tool is the feature the README leads with for choosing between near-identical
shots: select exactly two photos, right-click → **Image Compare**, and a window opens with both
images stacked. Moving the pointer left and right sweeps a vertical divider, revealing image A over
image B.

It is two small files — `Photonize/Views/CompareWindow.xaml(.cs)` (1200×800 window, header,
"✕ Close", Esc binding, status bar naming the left and right image) and
`Photonize/Views/ImageCompareControl.xaml(.cs)` (79 lines: image B underneath, image A clipped to
the pointer's X, a white drop-shadowed divider shown on hover, filename labels in the corners).

macOS has nothing. `macos/SETUP.md:164` lists it under "Not Yet Implemented" and
`macos/README.md:276` marks it "Planned".

This is the cheapest self-contained slice in the roadmap — no services, no file I/O, no new
dependency. Worth doing early for visible progress while the heavier drag work in slice 06 is
still ahead.

**Outcome:** two selected photos, right-click → Image Compare, a window with a mouse-driven A/B
divider, Esc to close.

---

## Design

### The window

macOS 13 supports the `Window` and `WindowGroup(id:for:)` scene types, so this needs no AppKit:

```swift
// PhotonizeApp.swift
WindowGroup(id: "compare", for: ComparePair.self) { $pair in
    if let pair { ImageCompareView(pair: pair) }
}
.defaultSize(width: 1200, height: 800)   // matches CompareWindow.xaml
```

`ComparePair` is a small `Codable & Hashable` struct of two file URLs — the value a
`WindowGroup(for:)` presents. Open it with `@Environment(\.openWindow)`.

Two windows opened for the same pair reuse one window, which is the behaviour you want.

Sandbox note: the compare window resolves the URLs directly. They are inside the folder the main
window has security-scoped access to, so no extra bookmark is needed — but the compare window must
not outlive that access. Load both images into memory on appear and hold `NSImage`s, not URLs.

### The comparison view

```
ZStack {
    Image(nsImage: imageB).resizable().scaledToFit()      // underneath
    Image(nsImage: imageA).resizable().scaledToFit()      // on top, masked
        .mask(alignment: .leading) {
            Rectangle().frame(width: dividerX)
        }
    if isHovering { divider line at dividerX }
}
```

- `GeometryReader` for the frame; `.onContinuousHover { phase in … }` gives the pointer location
  and the enter/exit transitions in one modifier — the direct SwiftUI equivalent of WPF's
  `MouseMove` + `MouseEnter`/`MouseLeave`.
- The divider is a 2 pt white line with a drop shadow, matching `ImageCompareControl.xaml`. It
  appears on hover and fades on exit.
- Filename labels bottom-left (A) and bottom-right (B), small, over a translucent capsule so they
  stay readable on any image.
- Both images are drawn `scaledToFit` into the same frame so the mask lines up. When the two
  images have different aspect ratios, fit both to the union frame rather than stretching either —
  a like-for-like comparison of an edited and unedited shot must not distort one of them.
- Black background, like the existing `PreviewPane` (`macos/Photonize/Views/PreviewPane.swift`).

### Loading

Load both at full resolution off the main actor via `ThumbnailGenerator.generatePreview` (slice 02
made it orientation-aware). Show a progress indicator until both are in. For very large images,
downsample to roughly the window's backing size — comparing two 60 MP files at full resolution
gains nothing visible and costs half a gigabyte.

### Menu wiring

Fill the `Image Compare` insertion point slice 04 left in `PhotoContextMenu`, enabled only when
the selection is exactly two non-folders — the same rule as Windows `CanCompareImages`. Add a
matching **View ▸ Compare Selected** menu item with a shortcut so it is discoverable, following
the `CommandGroup` pattern from slice 01.

Left/right assignment follows display order: the earlier photo in the grid is A (the one the
divider reveals), matching Windows, which passes the selection in order.

### Closing

`.keyboardShortcut(.cancelAction)` on a Close button gives Esc for free and puts a real button in
the window, matching the WPF "✕ Close". A status line under the image names both files, as the
Windows status bar does.

---

## Files

**New**
- `macos/Photonize/Views/ImageCompareView.swift` — the A/B divider view
- `macos/Photonize/Models/ComparePair.swift` — `Codable & Hashable` pair of URLs

**Modified**
- `macos/Photonize/PhotonizeApp.swift` — the `compare` `WindowGroup` scene, View menu item
- `macos/Photonize/Views/PhotoGridView.swift` — fill in the Image Compare context-menu item
- `macos/Photonize/ViewModels/PhotoGridViewModel.swift` — `canCompare` (exactly two non-folders)
  and the pair accessor
- `macos/README.md`, `macos/SETUP.md` — Image Compare moves from Planned to done

---

## Tests

Little here is unit-testable — it is a hover-driven view. What is worth pinning:

- `canCompare` is true for exactly two non-folder selections and false for one, three, or a
  selection containing a folder.
- The pair is ordered by `displayOrder`, so A is always the earlier photo.

## Verification

1. **Enablement.** Select one photo — Image Compare is disabled. Select two — enabled. Select
   three — disabled. Select a photo and a folder — disabled.
2. **The sweep.** Open two similar photos. Moving the pointer across the window sweeps the divider
   smoothly with no tearing or lag. At the far left you see only B; at the far right only A.
3. **Hover behaviour.** Move the pointer off the image — the divider fades out. Move back — it
   returns.
4. **Mismatched aspect ratios.** Compare a portrait and a landscape shot. Neither is stretched;
   both are letterboxed into the same frame.
5. **Orientation.** Compare two photos with EXIF orientation 6 — both upright.
6. **Close.** Esc closes the window. So does the Close button. The main window is unaffected.
7. **Reopen.** Compare the same pair twice — one window, brought to the front, not two.
8. **Large files.** Compare two 60 MP images. It opens within a couple of seconds and does not
   balloon memory.
9. **Dark and light.** Check both appearances — the white divider and the labels must stay legible.

---

## Out of scope

- Zoom and pan within the comparison. Windows does not have it; the tool is for "which of these
  two is better", not pixel-peeping.
- Horizontal (top/bottom) divider, or a side-by-side mode.
- Comparing more than two images.
- A difference/blend mode.
