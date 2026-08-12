# Changelog

All notable changes to Photonize will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [Unreleased]

### Changed
- **macOS: Removed the App Sandbox**: Photonize is distributed as a GitHub DMG, not through the Mac App Store, so the sandbox was only ever a liability. Without a paid Developer ID its security-scoped bookmarks could not be validated against a stable code-signing identity, so restoring the last folder, "Open With" on a folder, the Finder "Open in Photonize" service, and dragging files in all failed under the ad-hoc signature — the app had to be pointed at every folder through Browse… each launch. The app now opens any folder the user navigates to directly. The hardened runtime stays enabled (what notarization needs). This fixes: last-folder restore on launch; "Open With Photonize" / Dock / `open -a` / the Finder service on a folder; and drag-to-import.

### Fixed
- **macOS: Thumbnails stuck on the loading placeholder**: grid cells kept showing the spinner/grey placeholder until an unrelated relayout (e.g. toggling the inspector) forced a redraw. `PhotoItem`'s equality only compared `id`, so SwiftUI treated a late-arriving thumbnail (and new tags or renames) as "no change" and skipped repainting the cell. Equality now covers every rendered field; drag-reordering keys off `id` so it is unaffected.
- **macOS: Quick-tag digits (0-9) went to the address bar**: the directory field held keyboard focus, so pressing a digit to tag a selected photo typed into the path instead. The grid now catches its keys with a local event monitor that yields only while a text field is actually being edited, and clicking a photo takes focus off the address/prefix fields.

### Added
- **Windows: Drag photos out to File Explorer**: Photos and folders can now be dragged from the grid straight into an Explorer window, which **moves** them there
  - A photo's `.txt` caption travels with it, so captions are never orphaned
  - Multi-select drags the whole selection; hold **Ctrl** while dropping to copy instead of move
  - The `..` tile can't be dragged, so a drag can never hand Explorer the parent folder — and it is filtered out of a mixed selection
  - The grid refreshes once the files have gone and their quick-tags are pruned from `.photonize-tags.json`
  - Photonize never deletes the originals itself: it advertises the move and lets Explorer perform it, so a cancelled or misread drop can't lose files. A drop target that only copies leaves the original in place

- **macOS: Launch and Finder integration**: Photonize can now be opened straight into a folder from outside the app, matching the Windows Explorer verbs
  - `open -a Photonize <folder>`, dragging a folder onto the Dock icon or the app in Finder, and **Open With → Photonize** all navigate to that folder
  - Command-line flags via `--args`: `-d`/`--directory <folder>` opens a folder *instead of* restoring the last-used one (while keeping thumbnail size, prefix and pane layout), and `-e`/`--export-webp <file…>` exports images straight to WebP and reveals the output folder
  - Opening images (rather than a folder) navigates to their containing folder and pre-selects them
  - Two Finder right-click **Services** appear once the app has been launched from `/Applications`: **Open in Photonize** (folders) and **Export to WebP** (images)
  - **Export to WebP handles the whole selection in one app instance** — an improvement over the Windows verb, which launches one instance per selected file
  - A folder opened this way is remembered, so it reopens on the next launch; unknown arguments (e.g. Xcode's debugger flags) are ignored, and every entry point reuses the existing window rather than opening a second one
- **macOS: Prompt/caption text editor**: Each image can carry a caption in a `.txt` sidecar file (`photo.jpg` ↔ `photo.txt`) for AI-training workflows, matching Windows
  - The right-hand pane is now an inspector with **Preview** and **Text** tabs (a segmented switch), replacing the Windows two-panes/two-toggles arrangement; the tab you last used is remembered
  - The Text tab loads the selected image's caption, auto-saves as you type (debounced, not per keystroke), and deletes the `.txt` file when you empty it so a trainer never sees a blank caption
  - Switching photos mid-edit saves the outgoing caption against its own file before loading the next, so text can't cross-contaminate
  - Folders and videos show a disabled message rather than an editor
  - **The caption follows its image through a rename** — Apply Rename now renames `photo.txt` alongside `photo.jpg` instead of leaving it orphaned (a correctness improvement over the Windows client)
- **macOS: Video support**: MP4, MOV and M4V clips now appear and behave like photos, matching Windows — and with no FFmpeg dependency (AVFoundation decodes them natively)
  - Videos show real thumbnails with correct aspect ratio (portrait phone clips appear upright); a play-triangle badge marks them at a glance, and an unplayable file shows a broken-video placeholder
  - Selecting a video plays it in the preview pane with custom transport controls: play/pause, a scrubbable timeline that seeks on release, an `mm:ss / mm:ss` label, and a volume slider (default 0.8)
  - Playback auto-starts on selection and resets to the start at the end rather than looping; switching selection or closing the pane stops it immediately
  - Videos are included in batch renames alongside photos, in one filename-sorted sequence
  - The video right-click menu offers Open, Show in Finder and Delete (no Export or Image Compare); exporting a mixed selection exports only the images and reports the videos as skipped
- **macOS: Import from Finder and drag-into-folders**: Drag-and-drop now carries files in and out of the grid, matching Windows
  - **Import** — drag image files from Finder onto the grid and choose whether to insert them at the drop position or distribute them evenly through the existing photos; the folder is copied in (originals are never moved) and renumbered with the current prefix
  - Guards mirror Windows: a folder must be open, a prefix must be set, and the drop must contain at least one supported image, each with a clear message
  - **Move into folders** — drag photos or folders onto a subfolder tile, or onto the `..` tile to move up; the target folder highlights while it can accept the drop
  - Moving a folder into itself or a descendant is refused, as is merging a folder into an existing folder of the same name; file-name collisions prompt once (Overwrite All / Skip / Cancel)
  - An image's `.txt` caption sidecar travels with it on a move
  - The grid auto-scrolls when a drag nears its top or bottom edge
- **macOS: File operations and context-menu parity**: The right-click menu now matches Windows per item kind
  - Images and videos offer Open, Show in Finder and Delete; folders add folder deletion; the `..` tile only offers Open
  - **Folders can now be deleted** — right-click a subfolder and Delete moves it and all its contents to the Trash (restorable with ⌘Z in Finder); previously there was no way to remove a subfolder from inside the app
  - Delete confirmations use per-kind wording (files, folders-and-contents, or a mixed count)
  - On a volume with no Trash (e.g. a network share) you are asked once whether to delete permanently, and the answer applies to the rest of the batch
  - **The directory field is editable** — type or paste a folder path and press Return to load it; invalid or inaccessible paths report in the status bar instead of interrupting with an alert
  - **Path breadcrumb** — a clickable trail above the grid jumps straight to any ancestor folder
- **macOS: Grid keyboard and selection parity**: The photo grid now behaves like a Finder icon view
  - Arrow keys move the selection and wrap at row edges; ⇧+arrow extends the selection from the anchor
  - Shift-click selects the inclusive range from the last-clicked photo (previously it only added the one photo)
  - ⌘A (Edit ▸ Select All) selects every file, excluding folders
  - Delete/Backspace (File ▸ Delete) moves the selection to the Trash — now wired to the key, not just the context menu
  - Return opens the focused photo or navigates into the focused folder
  - Status bar now shows the total photo count alongside the selection count
  - Quitting with an un-applied reorder now warns first (deleting does not trigger the warning)
- **macOS: Settings window (⌘,)**: A native Settings scene with a default thumbnail size control and a "restore last folder on launch" toggle
- **macOS: App icon**: The app now ships with the Photonize icon in the Dock and Finder
- **macOS: Quick tagging (0-9)**: Select photos and press a digit to badge them, matching Windows
  - The tag shows as an accent-coloured badge in each thumbnail's top-right corner, sized to stay legible from 100 px to 400 px
  - One tag per photo — the same digit again clears it, a different digit replaces it; digits only tag when the grid has focus, so they still type normally in the path field and caption editor
  - Tags are saved per directory in a `.photonize-tags.json` sidecar, keyed by filename so a moved or copied folder keeps them, and restored on reload — the file format is **identical to the Windows client**, so a folder tagged on one platform reads correctly on the other
  - Tags follow their photos through an Apply Rename, with no stale entries left behind, and are kept consistent when a tagged photo is dragged into another folder
  - Right-click **Tags** submenu: **Move tagged images into sub-folders** (each group and its `.txt` captions moved into a named sub-folder, badges carried across), **Sort by tag** (folders first, then ascending digit, then untagged), and **Clear tags**
- **Quick tagging with the 0-9 keys**: Select one or more photos in the grid and press a number key to tag them
  - The tag appears as a small badge in the top-right corner of each thumbnail
  - One tag per photo — pressing a different digit replaces it, pressing the same digit again clears it
  - Tags are saved per directory in a `.photonize-tags.json` sidecar file and restored when the folder is reloaded
  - Tags follow their photos through an Apply Rename
  - New right-click "Tags" submenu:
    - **Move tagged images into sub-folders** — prompts for a folder name per tag, then moves each group (and its `.txt` caption) into place
    - **Sort by tag** — regroups the grid by tag, with untagged photos last
    - **Clear tags** — removes every tag in the current directory
- **Auto-caption (Florence-2 via ComfyUI)**: Right-click any image and choose "Auto-caption" to generate a descriptive caption using a Florence-2 workflow on a ComfyUI server
  - Works on a single image or a multi-selection of images
  - Each caption is saved as a `.txt` file next to its source image (e.g. "photo.webp" → "photo.txt")
  - Progress dialog with cancellation while captions are generated
  - Prompts before overwriting captions that already exist
  - Server address (default `mordor:8000`) is persisted in settings
- **Text Editor for AI Training Prompts**: Built-in text editor for managing AI training prompts alongside images
  - Automatically loads and saves .txt files with the same base name as selected images (e.g., "photo.jpg" → "photo.txt")
  - Real-time auto-save as you type
  - Auto-delete empty text files when all content is removed
  - Toggle between preview and text editor modes with dedicated TXT button
  - Monospace font (Consolas) for better prompt editing
  - Seamless switching between images updates the text editor with corresponding prompt files

### Changed
- **macOS: Universal binary**: The macOS app now ships as a universal build running natively on both Apple Silicon (arm64) and Intel (x86_64) Macs, instead of Intel-only through Rosetta. The release DMG is named `Photonize-<version>-universal.dmg`, and CI fails the build if the binary is not actually universal.
- **macOS: One release for both platforms**: A `v*` tag now builds and publishes the Windows MSI and the macOS DMG to a single GitHub release with shared notes from `CHANGELOG.md`; the previously-documented but non-existent `release-macos.yml` / `macos-v*` flow is gone.
- **macOS: Test suite runs in CI**: A new `PhotonizeTests` XCTest target covers the renamer, tagging, sorter, importer, exporter, selection maths, prompt store, thumbnail loader and launch-argument parsing. `xcodebuild test` runs on every build and release, and a failing test blocks the release. The tagging suites mirror the Windows `TagTests` for cross-client parity.
- **macOS: Last-folder restore on launch**: The last-opened folder is reopened automatically on the next launch
- **macOS: Preferences persist immediately**: Thumbnail size and preview toggle are now saved as you change them, not only when a folder is loaded or renamed
- **macOS: Single File menu**: Open Folder… (⌘O) and Refresh (⌘R) now live in the standard File menu instead of a duplicate one
- **macOS: Project generation**: The Xcode project is now generated from `macos/project.yml` with XcodeGen
- **macOS: Faster, progressive thumbnails**: Opening a folder now shows the grid immediately with placeholders that fill in as thumbnails decode, instead of waiting for the whole folder. Thumbnails are decoded through a bounded, cancellable loader so switching folders no longer leaves stale loads running, and memory use stays bounded regardless of the folder's pixel count. Folders now use the system folder icon (including custom icons set in Finder).

### Fixed
- **macOS: Unreliable double-click**: Double-clicking a photo or folder now reliably opens/navigates instead of being swallowed by the selection click
- **macOS: Folder drag dead zone**: A folder tile in the middle of the grid no longer blocks reordering a photo past it
- **macOS: Crash when dragging a removed photo**: Deleting or moving a photo mid-drag no longer crashes the app
- **macOS: Sideways photos**: Photos with EXIF orientation now appear upright in both the grid and the preview pane
- **macOS: Blurry thumbnails after resizing**: Enlarging the thumbnail slider now re-renders sharp thumbnails automatically instead of upscaling a small bitmap until the next Refresh

## [0.1.23] - 2026-01-09

### Added
- **Video Support**: Full support for organizing and batch-renaming video files (MP4, MOV, M4V) alongside photos
  - Video thumbnails with proper aspect ratio preservation
  - Video preview with playback controls in the preview pane
  - Timeline scrubbing, play/pause, and volume controls
  - Video-specific context menu (without image-specific export options)
