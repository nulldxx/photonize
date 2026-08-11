# Slice 03 — Grid interaction parity

**Depends on:** 02. **Blocks:** 06, 07, 08, 10.

## Context

The macOS grid responds to two keyboard shortcuts, ⌘O and ⌘R, and both live in the menu bar.
Windows has eight, most of them handled in
`Photonize/MainWindow.xaml.cs:108-267` (`PhotoListBox_PreviewKeyDown` and
`HandleRowWrappingNavigation`), deliberately scoped to the photo list so typing in the path box or
the text editor does not swallow them.

`macos/README.md:110` already tells users that Delete and Backspace move photos to the Trash.
They do not — grep for `onDeleteCommand`, `onKeyPress` or `NSEvent.addLocal` finds nothing. Delete
is reachable only through the context menu.

Selection is half-built. ⌘-click toggles correctly, but shift-click just inserts the one item, with
the comment `// For simplicity, just add to selection` (`macos/Photonize/Views/PhotoGridView.swift:137`).

There are also three latent bugs in the grid that every later slice would inherit:

- **Tap gestures are in the wrong order.** `PhotoGridView.swift:119-124` attaches
  `.onTapGesture` before `.onTapGesture(count: 2)`. In SwiftUI the single-tap recogniser usually
  wins, so double-click-to-open and double-click-to-navigate are unreliable.
- **`PhotoDropDelegate.dropEntered` force-unwraps twice** (`PhotoGridView.swift:68-69`):
  `items.firstIndex(of: draggingItem)!`. If the dragged item is removed mid-drag — which slice 06
  makes far more likely — the app crashes.
- **Folders are drag dead zones.** The `.if(!photo.isFolder)` modifier
  (`PhotoGridView.swift:29`) attaches drag and drop only to non-folders, so a folder tile in the
  middle of the grid blocks reordering past it. That same `if` view-builder extension
  (`PhotoGridView.swift:4-13`) changes view identity between branches, a known cause of dropped
  drag state.

**Outcome:** the grid behaves like a Finder icon view — real range selection, arrow navigation,
Delete works, ⌘A does something sensible — and the drag substrate is sound enough for slice 06 to
build real import and move-to-folder on top of.

---

## Design

### Key handling

`.onKeyPress` is macOS 14+ and the deployment target stays at 13, so use an
`NSViewRepresentable` that installs a first-responder `NSView` subclass overriding `keyDown(with:)`:

```swift
struct KeyCatcher: NSViewRepresentable {
    var onKey: (KeyPress) -> Bool     // return true if handled
}
```

Place it as a zero-size background on the grid's `ScrollView` and make it accept first responder.
Prefer this to `NSEvent.addLocalMonitorForEvents` — a global monitor fires while the user is
typing in the directory field or (slice 09) the text editor, which is precisely the bug Windows
avoided by binding to the ListBox rather than the window.

Route every key through a single `handleKey(_:)` on the view model so slice 10 can add the 0-9
tagging case in one place.

| Key | Behaviour | Windows source |
|---|---|---|
| Delete / Backspace | Trash the selection (files and folders) | `Window.InputBindings` |
| ⌘A | Select **files only**, folders excluded | `PhotoListBox_PreviewKeyDown:111` |
| → / ← | Move selection with row wrapping | `HandleRowWrappingNavigation:167` |
| ↑ / ↓ | Move selection one row | (Windows gets this free from `WrapPanel`) |
| ⇧ + arrows | Extend selection from the anchor | — |
| Return | Open / navigate into the focused item | — |
| 0-9 | reserved for slice 10 | `PhotoListBox_PreviewKeyDown:151` |

Row wrapping needs a column count. Derive it in the grid from the container width and the current
cell width (`thumbnailSize` + padding + spacing) and publish it to the view model; the
`GridItem(.adaptive(minimum: 150, maximum: 400))` layout means it must be recomputed on resize,
not assumed.

### Selection model

Add a selection anchor to the view model:

```swift
@Published var selectedPhotos: Set<PhotoItem.ID> = []
private var selectionAnchor: PhotoItem.ID?
```

- **Plain click** — selection becomes `[id]`, anchor = id.
- **⌘-click** — toggle membership, anchor = id.
- **⇧-click** — select the inclusive range between the anchor's index and the clicked index,
  leaving the anchor put. With no anchor, behave as a plain click.
- **⌘A** — every non-folder id; anchor = first.
- **Arrow keys** — move a focus cursor and reset the selection to it; ⇧ + arrow extends from the
  anchor.

The preview follows the *last* item touched rather than the first in document order — the current
code picks `photos.first(where: selected.contains)` (`PhotoGridView.swift:145`), which jumps the
preview backwards when you ⌘-click a later photo.

### Gesture and drag fixes

- Replace the two `onTapGesture` modifiers with a single high-priority double-tap plus a simple
  tap, or use `.gesture(TapGesture(count: 2).exclusively(before: TapGesture()))`, so
  double-click-to-open reliably beats the selection tap.
- Guard `dropEntered` with `guard let from = …, let to = … else { return }` instead of force
  unwrapping.
- Drop the `.if(!photo.isFolder)` wrapper: attach the drop delegate to **every** cell and decide
  inside `dropEntered`/`performDrop` what a folder target means. In this slice a folder simply
  refuses reordering (no move yet); slice 06 turns it into a move target. This also removes the
  identity-changing `if` view-builder extension, which can go entirely.

### Menu and status-bar parity

- File menu gains **Delete** (⌫) alongside Open Folder… and Refresh; Edit menu gains
  **Select All** (⌘A) via `CommandGroup(replacing: .textEditing)` so it is discoverable and shows
  its shortcut. Both use the `CommandGroup(replacing:)` pattern established in slice 01.
- Status bar gains `Photos: N` next to the existing "N selected" (Windows shows both).
- **Unsaved-changes guard on quit.** Windows warns in `MainWindow_Closing`. Implement
  `applicationShouldTerminate(_:)` in `AppDelegate` (`macos/Photonize/PhotonizeApp.swift:37`),
  returning `.terminateLater` and presenting an alert when `hasUnsavedChanges` is set. Note the
  existing Windows nuance to preserve: deleting does **not** arm unsaved changes
  (`UpdateDisplayOrder(updateUnsavedChanges: false)`), only reordering and setting a prefix do.

---

## Files

**New**
- `macos/Photonize/Views/KeyCatcher.swift` — `NSViewRepresentable` key handler

**Modified**
- `macos/Photonize/Views/PhotoGridView.swift` — anchor-based selection, gesture ordering, drop
  delegate on all cells, guarded index lookups; delete the `if` view extension
- `macos/Photonize/ViewModels/PhotoGridViewModel.swift` — `selectionAnchor`, `handleKey`,
  `selectAllFiles`, `moveFocus(direction:extending:)`, column count, preview follows last touched
- `macos/Photonize/Views/ContentView.swift` — `Photos: N` in the status bar
- `macos/Photonize/PhotonizeApp.swift` — Delete and Select All menu items;
  `applicationShouldTerminate` guard
- `macos/README.md` — the Delete/Backspace claim at line 110 becomes true; document the new keys

---

## Tests

- Range selection: anchor at index 2, shift-click index 7 → exactly ids 2…7 selected; shift-click
  index 0 → exactly 0…2.
- ⌘A with three folders and five files selects five ids.
- Row wrapping: 4 columns, focus on index 3, `→` lands on 4; focus on index 4, `←` lands on 3;
  focus on the last item, `→` is a no-op.
- These are pure functions on the view model given a `[PhotoItem]` and a column count — keep them
  that way so slice 12 can test them without a window.

## Verification

1. **Range selection.** Click photo 3, shift-click photo 9 — seven selected. ⌘-click photo 5 —
   six selected, preview shows photo 5. Shift-click photo 1 — the range inverts around the anchor.
2. **⌘A.** In a folder with subfolders, ⌘A selects only the files; the status bar count matches.
3. **Delete.** Select two photos, press ⌫, confirm — both land in the Trash and the grid renumbers.
4. **Arrows.** Resize the window so the grid reflows to a different column count, then walk the
   whole grid with → from the first item to the last: it wraps at each row edge with no dead ends.
   ⇧+→ grows the selection.
5. **Double-click.** Double-click a folder ten times in a row — it navigates every time. Before
   this slice it is intermittent.
6. **Folder drag-through.** Reorder a photo from before a folder to after it. Previously the
   folder blocked the drag.
7. **No crash on drag.** Start dragging a photo, and with the drag in flight delete the file from
   Finder, then release. The app must not crash.
8. **Quit guard.** Reorder photos, ⌘Q — a warning appears. Cancel, Apply Rename, ⌘Q — it quits
   cleanly. Delete a photo, ⌘Q — no warning (matching Windows).

---

## Out of scope

- Drag payloads and external drops — slice 06 replaces the drop mechanism wholesale; this slice
  only stops the current one crashing and unblocks folder tiles.
- The 0-9 tagging keys — slice 10, hanging off `handleKey`.
- Context-menu contents — slice 04.
