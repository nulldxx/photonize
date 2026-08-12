# Slice 13 — Drag photos out to Finder (macOS)

**Status:** not started. Investigated 2026-08-12 alongside the Windows implementation of
the same feature, which shipped; this slice is the macOS half.

## What Windows now does

Photos and folders can be dragged from the grid straight into a File Explorer window,
which moves them there along with each photo's `.txt` caption. Multi-select drags the
whole selection; Ctrl-drop copies instead. Implementation lives in
`Photonize/Helpers/PhotoDragHandler.cs` + `DragPayloadBuilder.cs`, and the app never
deletes source files itself — it advertises `DROPEFFECT_MOVE`, lets Explorer perform its
optimised move, then checks which paths actually disappeared.

Note that `plans/06-import-and-move-to-folder.md` line 203 previously recorded "Windows
does not do this either" as the reason for leaving it out. That is now out of date.

## Why it doesn't work on macOS today

Not entitlements. The App Sandbox was removed in `79f2afc` and
`macos/Photonize/Photonize.entitlements` is an empty dict, so there is no
sandbox restriction to work around.

The blockers are in the drag layer itself, `macos/Photonize/Views/PhotoGridView.swift`:

1. **The payload is deliberately process-private.** `NSItemProvider(photoItemIDs:)`
   (lines 13-24) registers exactly one type — the custom UTI `com.photonize.photo-item`,
   declared in `Info.plist` — with `visibility: .ownProcess`, which prevents any other
   app from reading it. No `public.file-url` is ever written, so Finder sees nothing it
   can accept.
2. **SwiftUI's `.onDrag` can't express a move.** The dragging session's source is an
   internal SwiftUI view, and there is no hook equivalent to
   `draggingSession(_:sourceOperationMaskFor:)`. Finder is only ever offered `.copy`.
3. **One provider means one item.** `.onDrag` (and `.draggable`) carry a single
   `NSItemProvider`, so even with a file URL registered, a multi-photo selection would
   hand Finder only the primary photo.

There is no completion callback either — nothing analogous to
`DragDropOperationFinished` — so even a copy-out could not be detected in order to
refresh the grid or clean up.

## Approach

Replace the SwiftUI `.onDrag` on tiles with an AppKit-backed drag source:

- An `NSViewRepresentable` conforming to `NSDraggingSource`, initiating
  `beginDraggingSession(with:event:source:)` with one `NSDraggingItem` per selected
  photo. Each item's `NSPasteboardItem` carries **both** `public.file-url` (for Finder)
  and `com.photonize.photo-item` (for the existing in-app paths).
- `draggingSession(_:sourceOperationMaskFor:)` returns `.move` for
  `.outsideApplication` and `.move` for `.withinApplication`, matching current reorder
  behaviour.
- `draggingSession(_:endedAt:operation:)` refreshes the grid when `operation == .move`.
  Finder does the move itself, so — as on Windows — nothing should be deleted here.
- Include each photo's `.txt` sidecar in the dragged items, mirroring the Windows
  payload rules and the existing `SidecarFiles.moveCompanions`.

## Risk

This is the main reason it wasn't done alongside Windows. `PhotoGridView.swift` currently
owns the in-app reorder (`GridDropDelegate.dropEntered` live `items.move(fromOffsets:)`),
the folder-drop path into `PhotoGridViewModel.moveItems`, the import path, and
`DragAutoScroller` — all keyed off `.onDrag`/`.onDrop`. Replacing the drag initiator
means re-proving all of them. Budget for regression testing of reorder, folder-drop,
import and auto-scroll, not just the new gesture.

## Cheaper partial alternative

Register `public.file-url` on the existing `NSItemProvider` alongside the custom UTI, and
drop the `.ownProcess` visibility. Finder would then accept the drag — but as a **copy**,
and only for the primary photo of a multi-selection. Perhaps 10 lines. Worth doing only
if some cross-app drag matters more than matching the Windows move semantics.

## Verification (when built)

`macos/PhotonizeTests/` currently has no drag coverage at all — `grep -rn "[Dd]rag"`
returns nothing. `dragSet(startingFrom:)` and `importInsertIndex` are pure functions and
should get tests as part of this slice, along with the new pasteboard-item construction
(the Windows equivalent, `DragPayloadBuilder`, is fully unit tested).

Manual: drag one photo to Finder (moves, with its `.txt`); drag a multi-selection (all
move); drag a folder; cancel with Esc; and re-run the whole of slice 06's drag matrix to
confirm reorder, folder-drop and import still work.
