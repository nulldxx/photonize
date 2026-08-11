# Roadmap: macOS client to Windows parity

## Why this exists

Photonize ships two clients that have drifted badly apart.

| | Windows (WPF, .NET 8) | macOS (SwiftUI) |
|---|---|---|
| Source | ~7,500 lines | ~1,050 lines |
| Services | 11 | 3 |
| Commands | 19 `ICommand`s | 7 view-model actions |
| Dialogs / extra windows | 4 | 0 |
| Unit tests | `Photonize.Tests` (2 suites) | none |
| Last touched | continuously | commit `e7a030c`, late 2025 |

Everything added to Windows since the macOS port stalled — multi-format export, image compare,
photo import, drag-to-folder moves, video support, the prompt text editor, quick tagging — exists
only on Windows. `macos/README.md` and `macos/SETUP.md` both carry stale and mutually
contradictory accounts of the gap (the README's table claims drag-drop reordering works; SETUP.md
says it doesn't; the truth is in between).

These twelve slices close that gap. Each is a self-contained, shippable increment with its own
verification steps.

---

## The slices

| # | Slice | Ports from | Depends on |
|---|---|---|---|
| [01](01-foundations-project-and-sandbox.md) | Foundations: project file, sandbox, settings | — | — |
| [02](02-thumbnail-pipeline.md) | Thumbnail pipeline | `WorkerQueue.cs`, `ThumbnailGenerator.cs` | 01 |
| [03](03-grid-interaction-parity.md) | Grid interaction parity | `MainWindow.xaml.cs` | 02 |
| [04](04-file-operations-and-context-menus.md) | File operations and context menus | `MainWindow.xaml`, `MainViewModel.cs` | 02 |
| [05](05-image-export.md) | Image export (WebP / PNG / JPG) | `ImageExporter.cs`, `ProgressDialog.xaml` | 01, 02 |
| [06](06-import-and-move-to-folder.md) | Import and move-to-folder | `PhotoImporter.cs`, `PhotoDropHandler.cs` | 03, 04, 05 |
| [07](07-image-compare.md) | Image compare | `CompareWindow.xaml`, `ImageCompareControl.xaml` | 03 |
| [08](08-video-support.md) | Video support | Windows 0.1.23 | 02, 03, 04 |
| [09](09-prompt-text-editor.md) | Prompt text editor | Windows `[Unreleased]` | 01 |
| [10](10-quick-tagging.md) | Quick tagging (0-9) | `TagService.cs`, `TagSorter.cs` | 03, 05 |
| [11](11-launch-integration.md) | Launch integration | `App.xaml.cs`, `Product.wxs` | 01, 05 |
| [12](12-tests-and-packaging.md) | Tests and packaging | `Photonize.Tests`, `release.yml` | all |

### Dependency graph

```
01 foundations
 ├── 02 thumbnails
 │    ├── 03 grid interaction ──┬── 06 import & move  (also needs 04, 05)
 │    │                         ├── 07 compare
 │    │                         ├── 08 video          (also needs 04)
 │    │                         └── 10 tagging        (also needs 05)
 │    ├── 04 file ops & menus ──┴── 06, 08
 │    └── 05 export ───────────────  06, 10, 11
 └── 09 text editor

11 launch integration — needs 01 for bookmarks, 05 for the Export to WebP verb
12 tests & packaging — last
```

Two pieces are built once and consumed repeatedly. Do not reorder around them:

- **The bounded-concurrency loader** (slice 02) — used by export, video thumbnails, and any
  future batch operation.
- **`ProgressSheet` / `OperationProgress`** (slice 05) — the macOS answer to
  `Views/ProgressDialog.xaml`, reused by import (06) and move-tagged-to-subfolders (10).

### Suggested order

`01 → 02 → 03 → 04 → 05 → 07 → 09 → 06 → 08 → 10 → 11 → 12`

Slices 07 and 09 are cheap and self-contained; doing them early gives visible progress while the
heavier drag-and-drop work in 06 is still ahead.

---

## Decisions already taken

**WebP via libwebp (SPM); deployment target stays macOS 13.**
ImageIO only gained WebP *encoding* in macOS 14 (Sonoma). Rather than drop Ventura users, slice 05
adds the project's first third-party dependency. This is the main reason slice 01's project-file
work has to come first.

**Native macOS idioms, not a pixel-copy of the WPF layout.**
The current UI is a custom `VStack` toolbar that will only get more crowded as features land.
Slices move it toward `.toolbar`, a real Settings scene (⌘,), an inspector with Preview/Text tabs,
and `NSServices` in place of the MSI's Explorer registry verbs. Where a slice deliberately
diverges from Windows behaviour it says so and gives the reason.

**Keep Trash, not Windows' permanent delete.**
Windows uses `File.Delete` / `Directory.Delete(recursive: true)`. macOS trashes. That difference
stays — it is the platform-correct behaviour and users expect it.

---

## Deliberately out of scope

These are real Windows features that are **not** being ported. They are listed so nobody thinks
they were forgotten.

### Topaz Photo AI upscaling
`Services/TopazPhotoAIService.cs`, the `PhotoAI Upscale` / `PhotoAI Upscale Folder` menu items,
and `docs/Topaz.md`. Excluded by decision. It shells out to
`C:\Program Files\Topaz Labs LLC\Topaz Photo AI\tpai.exe`; a macOS port would need the Mac Topaz
CLI, its own install detection, and its own retry/timeout handling for a feature only some users
have licensed.

### Auto-caption via ComfyUI (Florence-2)
`Services/CaptionService.cs` and the `Auto-caption` context-menu item. Not selected for this
roadmap, but genuinely portable later — it is plain HTTP against a ComfyUI server
(`POST /upload/image`, `POST /prompt`, poll `GET /history/{id}`) with an embedded workflow JSON.

If it is picked up, it needs two things that other slices should leave room for:

- the `com.apple.security.network.client` sandbox entitlement (slice 01 touches
  `Photonize.entitlements`);
- a server-address field defaulting to `mordor:8000` in the Settings scene (slice 01) and a
  `captionServer` key in `AppSettings`. Windows persists this key with **no UI at all** — the Mac
  version should do better.

Slice 09's `.txt` sidecar editor and slice 10's "move the caption alongside the image" behaviour
are the two places auto-caption would plug into, and both are in scope.

---

## Gap reference

Everything below exists on Windows. Every row is covered by a slice or listed above as deferred.

### Commands (`Photonize/ViewModels/MainViewModel.cs:484-502`)

| Command | Slice |
|---|---|
| `BrowseDirectoryCommand`, `LoadPhotosCommand`, `RefreshCommand` | exists |
| `ApplyRenameCommand` | exists (hardened in 04) |
| `OpenPhotoCommand`, `ShowInExplorerCommand`, `DeletePhotoCommand` | exists (extended in 04) |
| `TogglePreviewCommand` | exists (reworked into the inspector in 09) |
| `ExportWebPCommand`, `ExportPNGCommand`, `ExportJPGCommand` | 05 |
| `CompareImagesCommand` | 07 |
| `ToggleVideoPlaybackCommand` | 08 |
| `ToggleTextEditorCommand` | 09 |
| `MoveTaggedToFoldersCommand`, `SortByTagCommand`, `ClearTagsCommand` | 10 |
| `ExportPhotoAICommand` | **deferred** (Topaz) |
| `AutoCaptionCommand` | **deferred** (ComfyUI) |

### Keyboard shortcuts

| Windows | macOS | Slice |
|---|---|---|
| Ctrl+O browse | ⌘O — exists | — |
| F5 refresh | ⌘R — exists | — |
| Delete | missing (README claims otherwise) | 03 |
| Ctrl+A, files only | missing | 03 |
| ← / → with row wrapping | missing | 03 |
| 0-9 quick tag | missing | 10 |
| Enter in directory box | missing | 04 |
| Esc in compare window | n/a | 07 |

### Context menus

Windows has three `DataTrigger`-selected variants; macOS has one three-item menu.

| Variant | Items | Slice |
|---|---|---|
| Image | Show in Explorer, Image Compare, Export ▸, Auto-caption, Tags ▸, Delete | 04 (+05, 07, 10) |
| Video | Show in Explorer, Tags ▸, Delete | 08 |
| Folder | Show in Explorer, Export ▸, Tags ▸, Delete (recursive) | 04 |

### Settings (`Photonize/Services/SettingsService.cs:7-14`)

| Key | macOS today | Slice |
|---|---|---|
| `LastDirectoryPath` | present, but broken under sandbox | 01 (bookmarks) |
| `LastThumbnailSize` | present, saved at the wrong times | 01 |
| `LastPrefix` | present | 01 |
| `IsPreviewVisible` | present, saved at the wrong times | 01 |
| `IsTextEditorVisible` | missing | 01 / 09 |
| `CaptionServer` | missing | **deferred** |

Sidecar state, outside settings: `.photonize-tags.json` per directory (slice 10) and `<name>.txt`
prompt files per image (slice 09).

---

## Working notes

- The macOS toolchain is not available on the Linux dev box. Every slice's verification section
  assumes a Mac with Xcode 15+; build with
  `cd macos && xcodebuild -project Photonize.xcodeproj -scheme Photonize -configuration Release build`.
- `macos/Photonize/Services/FileRenamer.swift` is already a faithful port of the Windows two-pass
  algorithm. Reuse it; do not rewrite it.
- `detectCommonPrefix` and `filesAlreadyUsePrefix` in `PhotoGridViewModel.swift` already match
  Windows semantics, including "skip the rename confirmation on a pure reorder". Preserve that.
- There is no viable shared code layer between C# and Swift. Parity is maintained by porting
  behaviour and keeping the pure logic (`FileRenamer`, `TagSorter`) structurally identical and
  unit-tested on both sides.
