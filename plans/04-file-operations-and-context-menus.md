# Slice 04 — File operations and context menus

**Depends on:** 02. **Blocks:** 06, 08.

## Context

Windows shows one of three context menus, chosen by `DataTrigger` on the item
(`Photonize/MainWindow.xaml:233-395`):

| Variant | Items |
|---|---|
| Image | Show in Explorer · Image Compare · Export ▸ · Auto-caption · Tags ▸ · Delete |
| Video | Show in Explorer · Tags ▸ · Delete |
| Folder | Show in Explorer · Export ▸ (PhotoAI Upscale Folder) · Tags ▸ · Delete (recursive) |

macOS shows one menu with three items — Open, Show in Finder, Delete — and Delete is disabled for
folders entirely (`macos/Photonize/Views/PhotoGridView.swift:176`), so there is no way to remove a
subfolder from inside the app. Windows added folder deletion in commit `32ac6f0`.

Three smaller gaps sit alongside:

- **The directory field is read-only** (`macos/Photonize/Views/ContentView.swift:48`). Windows lets
  you type or paste a path and press Enter (`DirectoryPathTextBox_PreviewKeyDown`), which is how
  most people move between known folders.
- **Rename filename logic is duplicated.** `applyRename`
  (`macos/Photonize/ViewModels/PhotoGridViewModel.swift:170-178`) recomputes
  `"\(prefix)-\(String(format: "%05d", index + 1)).\(ext)"` independently of `FileRenamer`, which
  has just done the same thing. Two copies of the naming rule will drift.
- **Prefix validation is too permissive.** `FileRenamer.validatePrefix`
  (`macos/Photonize/Services/FileRenamer.swift`) rejects only `:`, `/` and `\`. `..`, a leading
  dot, and trailing spaces all pass. Windows uses the full `Path.GetInvalidFileNameChars()` set.

There is also a piece of near-dead code worth resolving: `checkIfCanUseTrash`
(`PhotoGridViewModel.swift:258-271`) calls
`FileManager.url(for: .trashDirectory, in: .userDomainMask, appropriateFor:, create: false)`,
which essentially never throws. The scary "PERMANENTLY delete — this cannot be undone" branch it
guards (lines 206-213) is therefore unreachable in practice, while the actual failure case —
a network volume with no `.Trashes` — surfaces as a per-file error instead.

**Outcome:** the right-click menu matches Windows for every item kind, folders can be deleted,
paths can be typed, and the rename naming rule lives in exactly one place.

---

## Design

### Context menu variants

Build the menu from the item kind rather than branching inside one `ViewBuilder` soup. A small
enum keeps slices 05, 07, 08 and 10 from tangling as they each add entries:

```swift
enum PhotoKind { case image, video, folder, parent }   // `parent` is the ".." tile
extension PhotoItem { var kind: PhotoKind { … } }
```

`PhotoContextMenu` switches on `photo.kind`. Where the menu acts on a selection rather than the
clicked item, follow the Windows rule already used in `PhotoContextMenu`
(`PhotoGridView.swift:167-171`): if the right-clicked item is not in the selection, make it the
selection first.

| Item | image | video | folder | `..` | Added by |
|---|:-:|:-:|:-:|:-:|---|
| Open / Open in Finder | ● | ● | ● | ● | this slice |
| Show in Finder | ● | ● | ● | — | this slice |
| Image Compare | ● | — | — | — | slice 07 |
| Export ▸ | ● | — | ● | — | slice 05 |
| Tags ▸ | ● | ● | ● | — | slice 10 |
| Delete | ● | ● | ● | — | this slice |

Leave clearly marked insertion points so the later slices are one-line additions.

### Deleting folders

Extend `deleteSelectedPhotos` to accept folders. Windows uses distinct confirmation wording for
files, folders, and mixed selections; mirror that:

- files only — "Move N files to the Trash?"
- folders only — "Move N folders and all their contents to the Trash?"
- mixed — name both counts.

`FileManager.trashItem(at:resultingItemURL:)` handles directories recursively, so no separate
code path is needed. Never allow deleting the `..` entry.

Failures accumulate into the existing summary alert (`PhotoGridViewModel.swift:248-253`) rather
than aborting the batch — that is already the Windows behaviour.

### Trash detection

Replace `checkIfCanUseTrash` with something that reflects reality. Simplest correct approach:
**always attempt `trashItem`, and fall back per file**. If it throws, ask once whether to delete
permanently instead, then apply that answer to the rest of the batch. That gives the honest
warning at the moment it actually matters and deletes the unreachable branch.

Keep the platform divergence explicit: Windows deletes permanently (`File.Delete`), macOS trashes.
Trash is correct here and stays.

### Editable directory field

- `TextField` becomes editable, `.onSubmit` loads the path (Windows commits on Enter and drops
  focus).
- Invalid or non-existent paths set `statusMessage` rather than throwing an alert — again matching
  Windows, which auto-loads only on a valid path.
- Native addition: a breadcrumb above or in place of the raw path, each component clickable to
  jump up the tree. `..` navigation exists but is a poor way to climb four levels.
- Sandbox note: typing a path the user has never granted access to will fail. Catch it and set a
  status message that tells them to use Browse… — the bookmark service from slice 01 only covers
  folders reached through `NSOpenPanel` or a Finder-originated open.

### Deduplicate the rename naming rule

Give `FileRenamer` the single source of truth:

```swift
func targetName(prefix: String, index: Int, extension ext: String) -> String
func renameFiles(_ items: [PhotoItem], prefix: String) async -> RenameResult
```

and have `RenameResult` carry the resulting `[(id: PhotoItem.ID, url: URL)]` so `applyRename`
updates the model from what actually happened on disk instead of recomputing it. This matters
beyond tidiness: slice 10 needs tags to follow files through a rename, and it can only do that
reliably if there is one authoritative mapping.

### Prefix validation

Match Windows' rejection set: the POSIX-illegal `/` and NUL, plus `:` (Finder still treats it
specially), plus leading/trailing whitespace, plus the reserved names `.` and `..`, plus a leading
`.` (which would make every renamed file invisible). Reject an empty result after trimming.
Surface the reason in the alert rather than a generic "invalid prefix".

---

## Files

**Modified**
- `macos/Photonize/Models/PhotoItem.swift` — `PhotoKind` and `var kind`
- `macos/Photonize/Views/PhotoGridView.swift` — `PhotoContextMenu` switches on kind, with marked
  insertion points for slices 05, 07, 10
- `macos/Photonize/ViewModels/PhotoGridViewModel.swift` — folder-aware delete with per-kind
  wording, trash fallback replacing `checkIfCanUseTrash`, `applyRename` consumes `RenameResult`,
  path-field submit handling
- `macos/Photonize/Services/FileRenamer.swift` — `targetName`, richer `RenameResult`, hardened
  `validatePrefix`
- `macos/Photonize/Views/ContentView.swift` — editable path field with `.onSubmit`, breadcrumb

---

## Tests

- `validatePrefix` rejects `..`, `.hidden`, `a/b`, `a:b`, `"  "`, `""`; accepts `holiday-2026`,
  `wedding_day`, `Photos 2026`.
- `targetName(prefix: "trip", index: 0, extension: "jpg")` == `"trip-00001.jpg"` — the off-by-one
  is the classic place for the two clients to diverge.
- `RenameResult` maps every input id to a URL that exists on disk after a round trip through a
  temp directory, including the swap case (`a→b`, `b→a`) the two-pass algorithm exists for.

## Verification

1. **Menu variants.** Right-click an image, a folder, and the `..` tile — three different menus,
   each with only the items in the table above.
2. **Folder delete.** Create a subfolder with photos inside, right-click → Delete, confirm. The
   folder and contents go to the Trash and can be restored with ⌘Z in Finder.
3. **Mixed selection.** Select one photo and one folder, Delete — the confirmation names both
   counts.
4. **Trash fallback.** Mount an SMB share with no `.Trashes`, put photos on it, delete one. You
   get an honest "cannot use the Trash here — delete permanently?" prompt once, and the answer
   applies to the batch.
5. **Path field.** Paste `~/Pictures` and press Return — it loads. Type nonsense — the status bar
   says so and the grid is untouched. Click a breadcrumb component — it navigates up.
6. **Rename dedup.** Reorder five photos, Apply Rename, and confirm the in-app names match
   `ls` exactly. Then reorder into a straight swap of two files and confirm both survive.
7. **Prefix validation.** Try `..` and `.x` as prefixes — both refused with a specific message.

---

## Out of scope

- Export ▸ contents — slice 05.
- Image Compare menu item — slice 07.
- Tags ▸ — slice 10.
- Video menu variant contents — slice 08 (the `PhotoKind.video` case exists here but is only
  reachable once video files are recognised).
- Drag-onto-folder moves — slice 06.
