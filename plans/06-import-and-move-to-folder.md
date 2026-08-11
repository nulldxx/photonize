# Slice 06 — Import and move-to-folder

**Depends on:** 03, 04, 05. **Blocks:** nothing.

## Context

Two drag behaviours exist on Windows and neither exists on macOS. Both live in
`Photonize/Helpers/PhotoDropHandler.cs` (386 lines) with the file work in
`Photonize/Services/PhotoImporter.cs`.

**Importing from the file manager.** Drop image files from Explorer onto the grid and Windows asks
(`Views/ImportDialog.xaml`) whether to **insert at the drop position** or **distribute evenly**
through the existing set, then copies the files in and renumbers the whole folder with the current
prefix. Insert-at-drop-position was added recently (`04f7b26`); distribute interleaves new photos
between existing ones, which is how you merge two shoots.

**Moving into folders.** Drag photos — or other folders — onto a folder tile, or onto the `..`
tile, and they move there. Collisions prompt. Folder-into-existing-folder merges are explicitly
refused.

macOS has neither. It also cannot get them without replacing its drag substrate first, because the
current implementation does not actually use drag payloads at all:

```swift
.onDrag {
    draggingItem = photo                                        // ← the real mechanism
    return NSItemProvider(object: photo.id.uuidString as NSString)   // ← never read
}
.onDrop(of: [.text], delegate: PhotoDropDelegate(…))
```
(`macos/Photonize/Views/PhotoGridView.swift:31-42`)

Reordering works entirely off the `@State draggingItem` set in the closure; the `NSItemProvider`
payload is decorative. That is fine for in-grid reordering and useless for anything originating
outside the app, where there is no `draggingItem` to consult.

**Outcome:** files dropped from Finder are imported and renumbered; photos and folders can be
dragged onto folder tiles to move them; the grid auto-scrolls when you drag near its edges.

---

## Design

### Replace the drop mechanism

Register two payload types and branch on which arrived:

| Source | Payload | Meaning |
|---|---|---|
| In-grid drag | a custom UTI, e.g. `com.photonize.photo-item`, carrying the item ids | reorder, or move onto a folder |
| Finder / other apps | `UTType.fileURL` | import |

Declare the custom UTI under `UTExportedTypeDeclarations` in `Info.plist` so it is well-formed.
Carry the **whole selection's** ids, not just the dragged item — Windows drags multi-selections
and macOS should too.

Read the payload in `validateDrop`/`performDrop` rather than relying on `@State`. Keep
`draggingItem` only for the visual (the 0.5-opacity treatment at `PhotoGridView.swift:118`).

The drop delegate now attaches to every cell (slice 03 removed the `.if(!photo.isFolder)`
wrapper) and decides by target kind:

```
target is a photo      → reorder (existing dropEntered behaviour)
target is a folder/..  → move
payload is fileURLs    → import at this index
```

Plus a grid-background drop target for "import at the end" when the drop misses every cell.

### Import

New `macos/Photonize/Services/PhotoImporter.swift`, porting `PhotoImporter.cs`:

```swift
enum ImportMode { case insert(at: Int), distribute }

actor PhotoImporter {
    func importPhotos(_ sources: [URL],
                      into directory: URL,
                      existing: [PhotoItem],
                      prefix: String,
                      mode: ImportMode,
                      overwrite: @MainActor ([String]) async -> OverwriteOption,
                      progress: @MainActor (Int, Int, String) -> Void) async throws -> [PhotoItem]
}
```

Behaviour to preserve from the C# original:

- Deduplicate the source list; drop anything already in the target directory.
- Compute final positions first — `insert` splices at the index, `distribute` interleaves evenly
  through the existing sequence.
- Two-pass rename via `.tmp_import` suffixes, exactly as `FileRenamer` uses `.tmp_rename`, so
  incoming names cannot collide with existing ones.
- **Copy, never move** the sources. Windows copies; a drag from Finder that silently moved
  someone's originals would be a bad surprise.
- Renumber the whole folder to `prefix-NNNNN` at the end.
- Guards: refuse with a clear message when there is no target directory, no prefix, or no
  supported files in the drop — all three are Windows message boxes.

Reuse the slice-05 `ProgressSheet`/`OperationProgress` — import of a hundred RAWs is not instant.

New `macos/Photonize/Views/ImportModeSheet.swift`, the analogue of `ImportDialog.xaml`: "Insert N
photos at this position" vs "Distribute them evenly", with Cancel.

### Move into folders

Add to the view model:

```swift
func moveItems(_ ids: [PhotoItem.ID], into target: URL) async
```

- Refuse moving a folder into itself or into one of its own descendants.
- Refuse merging a folder into an existing folder of the same name — Windows explicitly does not
  support this, and half-merging directories is worse than refusing.
- On a filename collision, prompt (overwrite / skip / cancel), reusing `OverwriteOption`.
- **Move the `.txt` sidecar with its image.** Slice 09 creates these and slice 10's
  move-tagged-to-subfolders does the same thing; keep the "an image and its caption travel
  together" rule in one helper both can call.
- Slice 10 note: tags are stored per-directory in `.photonize-tags.json` keyed by filename, so a
  move must remove the entry from the source directory's sidecar and add it to the target's.
  Write `moveItems` with that hook in mind even though the sidecar does not exist yet.
- Accumulate failures into one summary alert; do not abort the batch.

Visual feedback: highlight the folder tile under the pointer while a move-capable drag hovers
(Windows draws a highlight adorner). Reordering shows an insertion indicator instead.

### Auto-scroll

Windows scrolls the grid when the pointer comes within 50 px of the top or bottom edge during a
drag, at 50 FPS, capped at 10 px per tick. Port the same numbers — they are tuned and there is no
reason to differ. Drive it from a `Timer` started on `dropEntered` at the scroll view's margins
and stopped on exit or drop.

---

## Files

**New**
- `macos/Photonize/Services/PhotoImporter.swift` — insert/distribute import with two-pass renumber
- `macos/Photonize/Views/ImportModeSheet.swift` — insert vs distribute
- `macos/Photonize/Services/SidecarFiles.swift` — "move/rename/delete an image's companions"
  helper, shared with slices 09 and 10

**Modified**
- `macos/Photonize/Views/PhotoGridView.swift` — real drag payloads, kind-aware drop delegate,
  folder highlight, insertion indicator, background drop target, edge auto-scroll
- `macos/Photonize/ViewModels/PhotoGridViewModel.swift` — `importPhotos`, `moveItems`, guards,
  progress ownership
- `macos/Photonize/Info.plist` — `UTExportedTypeDeclarations` for the internal drag UTI
- `macos/README.md`, `macos/SETUP.md` — import and move are implemented; correct the contradictory
  drag-and-drop claims at `README.md:268` and `SETUP.md:166`

---

## Tests

`PhotoImporter`'s position maths is pure and worth testing directly:

- `distribute` of 3 new photos into 9 existing produces the documented interleave and preserves
  relative order within each group.
- `insert(at: 0)` and `insert(at: count)` behave as prepend and append.
- Sources already inside the target directory are dropped from the batch.
- Duplicate URLs in the drop are deduplicated.
- A full import round trip in a temp directory ends with a contiguous `prefix-00001…N` sequence and
  no `.tmp_import` leftovers.

Move logic:

- Moving a folder into its own descendant is refused.
- Moving an image moves its `.txt` sidecar.

## Verification

1. **Import, insert.** Load a folder of 10 photos with prefix `trip`. Drag 3 JPEGs from Finder and
   drop between photos 4 and 5. Choose "Insert at this position". Result: 13 files named
   `trip-00001` … `trip-00013`, with the new ones at positions 5, 6, 7.
2. **Import, distribute.** Same setup, choose "Distribute evenly" — the 3 new photos are spread
   through the sequence.
3. **Originals untouched.** The source files are still in their original Finder location.
4. **Guards.** Drop files with no prefix set → a message asking for one, no files copied. Drop a
   PDF → "no supported image files found".
5. **Move into a folder.** Select 5 photos, drag onto a subfolder tile — the tile highlights, and
   on drop the photos are gone from the grid and present in the subfolder. Navigate in to confirm.
6. **Move up.** Drag photos onto `..` — they land in the parent.
7. **Move a folder.** Drag one subfolder onto another. Then try dragging it onto a folder that
   already contains a folder of that name — refused with a clear message. Then try dragging it into
   itself — refused.
8. **Sidecars.** Give a photo a `.txt` file (slice 09), move the photo — the `.txt` moves too.
9. **Collision.** Move a photo into a folder that already has a file of that name — prompt appears,
   and each answer behaves.
10. **Auto-scroll.** In a folder of 300 photos, drag one to the bottom edge — the grid scrolls
    smoothly and drops in the right place.
11. **Reordering still works.** Everything in slice 03's verification must still pass; this slice
    replaced the mechanism underneath it.

---

## Out of scope

- Dragging photos *out* of Photonize into Finder or another app. Windows does not do this either.
- Importing whole folders by dropping them (Windows filters folders out of imports — `1a609d2`).
- Copy-vs-move choice on import. Windows always copies; matching that avoids a destructive default.
- Undo. Neither client has it; Trash and the Finder's own undo are the safety net.
