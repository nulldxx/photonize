# Slice 09 — Prompt text editor

**Depends on:** 01. **Blocks:** nothing (but slice 06's sidecar helper and slice 10's tag-move both
assume `.txt` files exist).

## Context

Windows' newest-but-one feature is a text pane for AI training prompts. Select `photo.jpg` and the
pane loads `photo.txt` from beside it; type and it saves as you go; empty it completely and the
file is deleted. A **TXT** toolbar button toggles the pane.

From `CHANGELOG.md` `[Unreleased]`:

> Automatically loads and saves .txt files with the same base name as selected images · Real-time
> auto-save as you type · Auto-delete empty text files when all content is removed · Toggle between
> preview and text editor modes with dedicated TXT button · Monospace font (Consolas)

The workflow this serves: caption a training set, one `.txt` per image, then feed the folder to a
LoRA trainer. It is the reason the deferred ComfyUI auto-caption feature writes `.txt` files, and
the reason slice 10's "move tagged images into sub-folders" carries the `.txt` along with its
image.

macOS has none of it. There is a preview pane (`macos/Photonize/Views/PreviewPane.swift`) and
nothing else on the right-hand side.

**Outcome:** an inspector that shows the selected image's preview *and* its prompt file, with
debounced auto-save and empty-file cleanup.

---

## Design

### Native divergence: tabs, not mutually exclusive panes

Windows treats preview and text editor as two separate 500 px panes with two toolbar toggles, and
**opening one closes the other**. That is a workaround for running out of horizontal room in a
fixed layout, not a design.

Make them tabs in a single inspector instead:

```
┌──────────────┬───────────────┐
│              │ [Preview][Text]│
│    grid      ├───────────────┤
│              │  image  ·or·  │
│              │  text editor  │
└──────────────┴───────────────┘
```

One `HSplitView`, one visibility toggle, a `Picker` in `.segmented` style at the top of the
inspector. Both are one click away instead of two, and the layout stops shuffling. This is the
"native idioms" decision from `plans/README.md` applied at the smallest scale.

Settings keep two keys so state round-trips: `isPreviewVisible` (is the inspector open) and
`isTextEditorVisible` (which tab was last active). Slice 01 added the second key; this slice is
its only consumer.

### The editor

New `macos/Photonize/Views/PromptEditorPane.swift`:

- `TextEditor` with `.font(.system(.body, design: .monospaced))`. Consolas is a Windows font;
  SF Mono is the correct counterpart, and asking for it by design token rather than by name means
  it follows the user's terminal-font settings.
- Header reading `<basename>.txt`, matching the Windows pane header.
- Empty state "No image selected" when nothing is selected, and a distinct "No prompt yet — start
  typing" when an image is selected but has no `.txt`. Windows shows only the first; the second
  makes it obvious the pane is live.
- Disabled for folders and (per slice 08) videos, which have no meaningful prompt file.

### Loading and saving

New `macos/Photonize/Services/PromptFileStore.swift`:

```swift
struct PromptFileStore {
    static func url(for image: URL) -> URL      // photo.webp -> photo.txt
    func load(for image: URL) throws -> String  // "" when absent
    func save(_ text: String, for image: URL) throws   // deletes the file when text is empty
}
```

- **Debounced save**, ~500 ms after the last keystroke, not literally per character. Windows saves
  on every `TextChanged`; on a network volume that is a write storm. Also flush on selection
  change, on window resign-key, and on quit, so nothing is lost.
- **Delete when emptied** — including when the content is only whitespace. This is what Windows
  does and it matters: a trainer that sees an empty `.txt` may caption the image as "".
- Handle the selection changing mid-edit: flush the outgoing image's text *before* loading the
  incoming one. Windows had a bug here (`f9943e0`, "fix text content updates to avoid triggering
  auto-save") where loading a new file triggered a save against the previous one; guard the load
  with a flag so the debounce timer cannot fire against the wrong target.
- Read and write UTF-8 without a BOM, and preserve whatever line endings are already in the file —
  these are consumed by Python tooling.
- On a multi-selection, edit the **last-touched** item's prompt (slice 03 made the preview follow
  the last touched item; the two panes should agree). Show the count so it is clear which one.

### Keyboard

The slice-03 `KeyCatcher` sits on the grid, not the inspector, so typing in the editor cannot be
swallowed by the 0-9 tag handler (slice 10) or ⌘A. Verify this explicitly — it is the exact
failure Windows avoided by binding keys to the ListBox rather than the window, and it is the most
likely regression when slice 10 lands.

### Sidecar consistency

`PromptFileStore.url(for:)` becomes the single definition of "an image's prompt file". Slice 06's
`SidecarFiles` helper and slice 10's tag-move both call it, so the rename/move/delete rules stay in
one place. Note the follow-on work for whichever slice lands second: **renaming an image should
rename its `.txt`.** Windows does not currently do this — Apply Rename leaves orphaned prompt files
behind — so this is a small correctness win for the Mac client, and worth doing here inside
`FileRenamer` while the naming logic is fresh from slice 04.

---

## Files

**New**
- `macos/Photonize/Views/PromptEditorPane.swift` — the editor
- `macos/Photonize/Services/PromptFileStore.swift` — load/save/delete, canonical `.txt` path

**Modified**
- `macos/Photonize/Views/ContentView.swift` — inspector with Preview/Text tabs replacing the
  preview-only `HSplitView` branch; single visibility toggle
- `macos/Photonize/ViewModels/PhotoGridViewModel.swift` — `promptText`, debounced save, flush on
  selection change / resign-key / quit, `inspectorTab` persisted
- `macos/Photonize/Views/PreviewPane.swift` — becomes one tab rather than the whole pane
- `macos/Photonize/Services/FileRenamer.swift` — rename the `.txt` alongside its image
- `macos/README.md` — document the pane and the `.txt` convention

---

## Tests

- `PromptFileStore.url(for:)` maps `a/b/photo.webp` → `a/b/photo.txt`, and handles names with dots
  (`photo.v2.jpg` → `photo.v2.txt`).
- `save("")` and `save("   \n ")` both delete an existing file and create nothing when absent.
- `load` returns `""` for a missing file rather than throwing.
- Renaming `photo.jpg` → `trip-00001.jpg` also renames `photo.txt` → `trip-00001.txt`, and leaves
  nothing behind.

## Verification

1. **Round trip.** Select a photo, switch to the Text tab, type a caption, click another photo,
   click back — the caption is there. Confirm with `cat` in Terminal.
2. **Debounce.** Type continuously for ten seconds while watching the file's mtime — it updates
   periodically, not per keystroke.
3. **Delete on empty.** Select all the text, delete it, switch photos. The `.txt` file is gone from
   disk.
4. **No cross-contamination.** Type into photo A's prompt and immediately click photo B before the
   debounce fires. A's text must land on A's file, and B's editor must show B's content. This is
   the `f9943e0` bug class.
5. **Quit safety.** Type a caption and ⌘Q immediately — the text is saved.
6. **Tabs.** Toggle the inspector; the tab you were last on is restored. Quit and relaunch — still
   restored.
7. **Keyboard isolation.** With the Text tab focused, type `photo 5 of 9` — the digits go into the
   text, not into a tag (re-run this after slice 10). Type ⌘A — it selects the text, not the grid.
8. **Non-images.** Select a folder — the Text tab is disabled with a clear message. Same for a
   video once slice 08 lands.
9. **Rename.** Give three photos prompts, reorder them, Apply Rename. Each `.txt` follows its
   image and the numbering matches.
10. **Encoding.** Paste text with accents and emoji, save, and read the file back in Python with
    `open(..., encoding='utf-8')`.

---

## Out of scope

- Generating captions — deferred with the ComfyUI auto-caption feature (see `plans/README.md`).
  This slice is the editing surface such a feature would write into.
- Editing prompts for a multi-selection at once (bulk find-and-replace, appending a trigger word).
  Useful for LoRA work, but Windows has no equivalent and it is a separate feature.
- Syntax highlighting, tag autocomplete, or token counting.
- Any sidecar format other than `.txt` (`.caption`, `.json`).
