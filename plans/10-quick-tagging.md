# Slice 10 — Quick tagging (0-9)

**Depends on:** 03, 05. **Also touches:** 06, 09. **Blocks:** nothing.

## Context

The newest Windows feature (commit `c826eb4`). Select photos, press a digit, and they carry that
tag as a badge in the thumbnail's top-right corner. Tags persist per directory and drive three
operations.

From `CHANGELOG.md` `[Unreleased]`:

> One tag per photo — pressing a different digit replaces it, pressing the same digit again clears
> it · Tags are saved per directory in a `.photonize-tags.json` sidecar file and restored when the
> folder is reloaded · Tags follow their photos through an Apply Rename · New right-click "Tags"
> submenu: **Move tagged images into sub-folders** · **Sort by tag** · **Clear tags**

The workflow: sweep a shoot pressing 1 for keepers, 2 for maybes, 3 for rejects, then move each
group into its own subfolder in one action.

Three Windows files do the work — `Services/TagService.cs` (94 lines, sidecar persistence),
`Services/TagSorter.cs` (36 lines, pure ordering logic, deliberately kept free of WPF and file I/O
so it can be unit-tested) and `Views/TagFolderDialog.xaml`. macOS has none of it.

**Outcome:** full tagging parity, including sidecar files that are interchangeable between the two
clients.

---

## Design

### Model and persistence

`PhotoItem` gains `var tag: Int?` and `var hasTag: Bool` — the same shape as the Windows model
(`Photonize/Models/PhotoItem.cs:64-83`).

New `macos/Photonize/Services/TagService.swift`, a direct port:

```swift
struct TagService {
    static let fileName = ".photonize-tags.json"
    func load(from directory: URL) -> [String: Int]        // filename -> digit
    func save(_ photos: [PhotoItem], to directory: URL)
}
```

Rules that must match Windows exactly, because the sidecar is a shared file format:

- **Keyed by filename, not path** — so a folder can be moved or copied and keep its tags. This is
  called out in the C# doc comment and is the whole reason the format works.
- **Values outside 0-9 are ignored on load.** Defensive, but keeps a hand-edited file from
  producing a badge that cannot be displayed.
- **Delete the sidecar when nothing is tagged**, rather than writing `{}`.
- Filename comparison is case-insensitive on Windows (`StringComparer.OrdinalIgnoreCase`). macOS
  filesystems are usually case-insensitive too, so match that — but do not *normalise* the case
  when writing, or a folder round-tripped between the two clients would churn.
- Pretty-printed JSON with sorted keys, so the file diffs cleanly in git.

The file is hidden (leading dot), and `loadPhotos` enumerates with `.skipsHiddenFiles`
(`PhotoGridViewModel.swift:79`), so it will not appear in the grid. Verify that.

### Applying tags

Hook the 0-9 case into `handleKey` from slice 03 — top-row digits and the numeric keypad, no
modifiers, exactly as `MainWindow.xaml.cs:151-165` does.

- Applies to the whole selection.
- Same digit again → clear. Different digit → replace. One tag per photo, never a set.
- Save the sidecar after each change (it is a tiny file; debouncing is not worth the complexity).
- Folders and the `..` tile cannot be tagged.

**Critical:** the key handler lives on the grid's `KeyCatcher`, not the window. With slice 09's
text editor open, typing `photo 5 of 9` must put digits in the text, not tag the selection. This is
the single most likely regression in this slice — Windows hit it and solved it the same way.

### Badge

Overlay on the thumbnail's top-right corner in `PhotoItemView`
(`macos/Photonize/Views/PhotoGridView.swift:83-125`). Windows uses keyed `TagBadgeStyle` /
`TagBadgeTextStyle` resources in `App.xaml`; the macOS equivalent is a small `Circle` with the
digit in a semibold monospaced-digit font, with enough contrast to read over any image — a filled
accent-coloured circle with a subtle shadow, sized relative to `thumbnailSize` so it stays legible
at 100 px and does not dominate at 400 px.

### Tags submenu

Fill the `Tags ▸` insertion point slice 04 left in all three context-menu variants (images,
videos and folders all get it on Windows). Three items, each disabled unless something in the
directory is tagged (Windows `HasTaggedPhotos`):

**Move tagged images into sub-folders.** New `macos/Photonize/Views/TagFolderSheet.swift`, the
analogue of `TagFolderDialog.xaml`: one row per tag in use, each showing the badge, an editable
folder name defaulting to the digit, and "N photos". Move/Cancel. On confirm:

- Create each folder under the current directory.
- Move each group's images **plus their `.txt` prompt files** (slice 09's `PromptFileStore.url` and
  slice 06's `SidecarFiles` helper give the canonical rule — call them, do not re-derive it).
- Write each group's tags into the destination folder's own sidecar, and remove them from the
  source's. Windows carries the tags across; do the same so navigating into `keepers/` still shows
  the badges.
- Reuse the slice-05 `ProgressSheet` — this is a bulk file operation and can take a while on a
  network volume.
- Validate folder names with the same hardened rules slice 04 gave `FileRenamer.validatePrefix`,
  and refuse if a name collides with an existing file.

**Sort by tag.** Port `TagSorter.cs` verbatim in structure — a pure, dependency-free function:

```swift
enum TagSorter {
    /// Folders (including "..") first, then tagged photos grouped by ascending digit,
    /// then untagged. Stable within each group by displayOrder, so sorting twice is a no-op.
    static func sortByTag(_ photos: [PhotoItem]) -> [PhotoItem]
}
```

Keep it free of file I/O and SwiftUI for exactly the reason the C# version is — it is the easiest
thing in the app to unit-test, and slice 12 tests it.

Sorting arms unsaved changes (it is a reorder), so Apply Rename follows naturally.

**Clear tags.** Confirmation, then remove every tag in the directory and delete the sidecar.

### Tags through a rename

Windows achieves this by mutating `PhotoItem` in place inside `FileRenamer`, so the in-memory tag
travels with the item and the post-rename `TagService.Save` writes the new filenames.

macOS's `PhotoItem` is a struct in an array, so do it explicitly: slice 04 gave `RenameResult` an
authoritative `[(id, newURL)]` mapping. Apply the new URLs to the items — which already carry their
tags — then re-save the sidecar. That is the whole fix, and it is the reason slice 04 deduplicated
the naming logic.

### Interaction with moves

Slice 06's `moveItems` must keep the two sidecars consistent: remove the entry from the source
directory's `.photonize-tags.json` and add it to the target's. Slice 06 flagged this hook; wire it
up here.

---

## Files

**New**
- `macos/Photonize/Services/TagService.swift` — sidecar load/save
- `macos/Photonize/Services/TagSorter.swift` — pure ordering
- `macos/Photonize/Views/TagFolderSheet.swift` — per-tag folder names

**Modified**
- `macos/Photonize/Models/PhotoItem.swift` — `tag`, `hasTag`
- `macos/Photonize/ViewModels/PhotoGridViewModel.swift` — `applyTag(_:)`, `hasTaggedPhotos`,
  `moveTaggedToFolders`, `sortByTag`, `clearTags`; load tags in `loadPhotos`; re-save after rename
- `macos/Photonize/Views/PhotoGridView.swift` — badge overlay, `Tags ▸` submenu in all three
  context-menu variants
- `macos/Photonize/Views/KeyCatcher.swift` / `handleKey` — the 0-9 case
- `macos/Photonize/Services/FileRenamer.swift` — sidecar re-save after a rename
- `macos/README.md` — document tagging and the sidecar file

---

## Tests

`TagService` and `TagSorter` are the best-testable code in the app. Mirror
`Photonize.Tests/TagTests.cs`:

- Save/load round trip preserves every tag.
- A sidecar containing `{"a.jpg": 42}` loads as untagged for `a.jpg`.
- Saving with no tagged photos deletes an existing sidecar.
- Loading from a directory with no sidecar returns empty, not an error.
- `sortByTag` puts folders first (with `..` first among them), then ascending digits, then
  untagged; is stable within groups; and is **idempotent** — `sortByTag(sortByTag(x)) == sortByTag(x)`.
- Tags survive a `FileRenamer` round trip, including a two-file swap.

## Verification

1. **Tag and clear.** Select three photos, press `1` — three badges. Press `1` again — cleared.
   Press `2` — badges show 2.
2. **Persistence.** Tag some photos, navigate away, navigate back — badges restored. Quit and
   relaunch — still there. `cat .photonize-tags.json` shows filename → digit.
3. **Cross-client.** Tag a folder on Windows, open it on macOS — the same badges appear, and vice
   versa. The file format must be identical.
4. **Portability.** Copy the whole folder elsewhere and open the copy — tags intact (this is why
   the key is a filename).
5. **Rename.** Tag five photos with mixed digits, reorder, Apply Rename. Badges follow the right
   photos, and the sidecar now lists the new filenames with no stale entries.
6. **Move to subfolders.** Tag photos 1/2/3, Tags ▸ Move tagged images into sub-folders. Name them
   `keepers`, `maybe`, `reject`. All three folders are created with the right photos, the `.txt`
   files come along, and navigating into `keepers` shows the badges still set to 1.
7. **Sort.** Tags ▸ Sort by tag — grid regroups, untagged last, folders still at the front. Sort
   again — nothing moves.
8. **Clear.** Tags ▸ Clear tags, confirm — all badges gone and `.photonize-tags.json` deleted from
   disk.
9. **Sidecar is invisible.** `.photonize-tags.json` never appears as a grid item.
10. **Keyboard isolation.** With slice 09's Text tab focused, type `taken at 5pm` — the digit lands
    in the text and nothing gets tagged. With the grid focused, the same keystroke tags.
11. **Badge legibility.** Check at 100 px and 400 px thumbnails, over a white photo and a black
    one, in light and dark appearance.
12. **Move consistency.** Tag a photo, drag it into a subfolder (slice 06), navigate in — the badge
    is there, and it is gone from the parent's sidecar.

---

## Out of scope

- More than ten tags, named tags, or colour labels. Windows is 0-9 and the badge has room for one
  character.
- Multiple tags per photo.
- Filtering the grid by tag (only sorting). Windows has no filter either.
- Reading or writing Finder tags / `com.apple.metadata:_kMDItemUserTags`. Tempting on macOS, but it
  would break the cross-client sidecar contract that makes item 3 above work.
