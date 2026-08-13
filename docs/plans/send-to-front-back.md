# DECK-1 — "Send to → Front / Back" context-menu submenu

Plan only. No implementation in this branch.

## Goal

Right-clicking a photo tile offers a **Send to** submenu with **Front** and **Back**.
*Front* moves the item to the start of the photo list, *Back* to the end. This is
list position (the order that drives batch rename), **not** z-order/stacking.

## What the codebase already gives us

Both clients store order the same way: the collection's own index is the truth, and
`displayOrder` is a renumbered mirror of it. Nothing persists order — it becomes
durable only when Apply Rename writes `{prefix}-NNNNN` in list order.

| | Windows (WPF) | macOS (SwiftUI) |
|---|---|---|
| Collection | `MainViewModel.Photos` — `ObservableCollection<PhotoItem>` (`Photonize/ViewModels/MainViewModel.cs:108`) | `PhotoGridViewModel.photos` — `@Published [PhotoItem]` (`macos/Photonize/ViewModels/PhotoGridViewModel.swift:7`) |
| Renumber hook | `UpdateDisplayOrder()` (`MainViewModel.cs:830`) | `updateDisplayOrder()` (`PhotoGridViewModel.swift:1341`) |
| Existing reorder | inline in `Helpers/PhotoDropHandler.cs:174-268` | inline in `GridDropDelegate.dropEntered` (`Views/PhotoGridView.swift:161-168`) |
| Context menu | 3 `ContextMenu` setters in `MainWindow.xaml:232-405` (image / video / folder `DataTrigger`s) | `struct PhotoContextMenu` (`Views/PhotoGridView.swift:492-599`), switches on `photo.kind` |
| Selection | `SelectionMode="Extended"`; `_selectedPhotos` pushed from `MainWindow.xaml.cs:78` | `selectedPhotos: Set<PhotoItem.ID>` (`PhotoGridViewModel.swift:13`) |
| Pure-helper test seam | `Services/TagSorter.cs` + `Photonize.Tests` | `nonisolated static` on the VM, or a free enum like `Services/TagSorter.swift` |

So there is **no** existing move-within-list command on either side — the only reorder
path is drag-and-drop, and on both platforms that arithmetic is buried in a
drop-handler that the test projects cannot reach.

## Approach

### 1. Extract a pure, tested reorder helper (both platforms)

This is the load-bearing decision. Add a pure function that takes the current list,
the set of items to move, and a destination end, and returns the new list. It owns
the two rules below (folder floor, multi-select order) so neither the VM nor the
drop handler has to.

Windows — `Photonize/Helpers/ListReorder.cs` (no `System.Windows` types, matching
`DragPayloadBuilder`'s constraint so `Photonize.Tests` can exercise it):

```csharp
public enum SendToEdge { Front, Back }

public static List<PhotoItem> SendTo(
    IReadOnlyList<PhotoItem> photos,
    IReadOnlyCollection<PhotoItem> moving,
    SendToEdge edge);
```

macOS — `macos/Photonize/Services/ListReorder.swift` with the same shape
(`enum SendToEdge { case front, back }`, `static func sendTo(...) -> [PhotoItem]`).

### 2. Wire a command / action on the view model

Windows: two `ICommand`s on `MainViewModel` using the existing hand-rolled
`RelayCommand<PhotoItem?>` pattern (`MainViewModel.cs:2443`) — `SendToFrontCommand`,
`SendToBackCommand`. Each resolves the target set exactly as `DeletePhoto` does
(`MainViewModel.cs:1024-1031`): prefer `_selectedPhotos` when the right-clicked item is
in it, otherwise fall back to the `CommandParameter`. Then `Photos.Clear()`, re-add the
helper's result, `UpdateDisplayOrder()` — the same dance `SortByTag()` already performs
(`MainViewModel.cs:1970-1990`). `CanExecute`: target set is non-empty, contains at least
one non-folder, and the move would actually change the order.

macOS: `func sendTo(_ edge: SendToEdge, photo: PhotoItem)` on `PhotoGridViewModel`,
reusing `dragSet(startingFrom:)` (`PhotoGridViewModel.swift:939`) to get selection-aware
targets for free, then assigning `photos = ListReorder.sendTo(...)` inside
`withAnimation(.default)` and calling `updateDisplayOrder()`.

### 3. Add the submenu

Windows — a nested `MenuItem` in the **image** and **video** context menus in
`MainWindow.xaml`, copying the `Export`/`Tags` submenu shape verbatim (VM retargeted via
`PlacementTarget.Tag`, manual `▶` glyph header, `PlacementTarget.DataContext` as
`CommandParameter`). The nested-binding trick already works because
`RelativeSource AncestorType=ContextMenu` walks to the root menu. `App.xaml:237-251`
deliberately leaves `MenuItem.Template` alone, so submenus need no styling work.
Insert next to `Export`, above `Tags`. **Not** added to the folder menu.

macOS — one private `var sendToMenu: some View` in `PhotoContextMenu`
(`Menu("Send to")` with two `Button`s), added to the `.image` and `.video` cases at the
insertion points the file's own comment at `PhotoGridView.swift:489` anticipates.

## Edge cases

- **The `..` tile and folder tiles.** Load order is: parent tile, then folders, then
  media (`MainViewModel.cs:634-692`; `PhotoGridViewModel.swift:326-388`). "Front" must
  therefore mean *first media slot* — index after the trailing leading-folder block —
  not index 0, or a photo lands above `..`. This is the one rule worth a dedicated test.
  Folders themselves get no Send-to menu, and are never part of a move set.
- **Right-click does not change selection (WPF).** So `_selectedPhotos` may not contain
  the clicked tile. Follow the `DeletePhoto` precedent: if the clicked item is outside
  the selection, act on the clicked item alone. macOS already implements exactly this in
  its `deleteButton` (`PhotoGridView.swift:588`) — mirror it.
- **Multi-select.** Moving N items must preserve their *relative* order at the
  destination; both edges are then unambiguous (no index-adjustment arithmetic as in
  `PhotoDropHandler`, since the destination is an end, not a neighbour).
- **Sort by tag.** `SortByTagCommand` fully rebuilds the list, so a subsequent sort
  discards a Send-to move. That is already true of drag-reorder, so no conflict is
  introduced — no change needed, but worth stating in the changelog.
- **Persistence.** Order is in-memory; a Send-to move sets `HasUnsavedChanges` via
  `UpdateDisplayOrder()` when a prefix is set, which is the existing and correct
  behaviour. No `AppSettings` change.
- **Import / refresh.** Refresh re-derives order alphabetically; imports insert by
  `DisplayOrder`. Neither needs touching.
- **No-op moves.** Sending an item already at the target edge should be disabled (or a
  silent no-op) rather than firing a spurious `HasUnsavedChanges`.

## Follow-on win

Once `ListReorder` exists, `PhotoDropHandler` and `GridDropDelegate` can delegate their
move arithmetic to it, which is the first time that logic becomes unit-testable. Worth
doing, but as a separate change — not folded into this one.

## Tests

- Windows, `Photonize.Tests/ListReorderTests.cs`: front skips the folder block; back
  appends; multi-select keeps relative order; folders are never moved; already-at-edge
  is a no-op; idempotence.
- macOS, `PhotonizeTests/ListReorderTests.swift`: the same six cases.

## Size

Small — roughly **250–350 lines across ~8 files**, half of it tests, and no new
dependency, no installer change (`Product.wxs` untouched), no `project.yml` change.

- Windows: `Helpers/ListReorder.cs` (new), `ViewModels/MainViewModel.cs`,
  `MainWindow.xaml`, `Photonize.Tests/ListReorderTests.cs` (new)
- macOS: `Services/ListReorder.swift` (new), `ViewModels/PhotoGridViewModel.swift`,
  `Views/PhotoGridView.swift`, `PhotonizeTests/ListReorderTests.swift` (new)
- `CHANGELOG.md` under `[Unreleased] → Added`

Each platform is independently shippable; Windows first is the natural order given the
context-menu XAML is the fiddlier half.

## Assumption to confirm

The board item says "the photonize repo" without naming a platform. This plan covers
**both** clients so behaviour stays in step. If only one is wanted, drop the other
platform's four files.
