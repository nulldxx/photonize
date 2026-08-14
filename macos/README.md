# Photonize for macOS

A macOS desktop application for visually organizing and batch-renaming photographs. Built with SwiftUI for a native macOS experience.

## Features

- **Visual Organization**: Browse photos with thumbnail previews and folder navigation
- **Drag-and-Drop Reordering**: Reorder photos by dragging them into your desired sequence
- **Batch Rename**: Rename multiple files at once with customizable prefix (e.g., `vacation-00001`, `vacation-00002`)
- **Smart Prefix Detection**: Automatically detects common filename patterns from existing files
- **Inspector Pane**: A right-hand pane with **Preview** and **Text** tabs — full-size previews with file details, or a prompt editor — toggled as a whole with the sidebar button
- **Prompt Editor**: Each image can carry a caption in a `.txt` sidecar file (`photo.jpg` ↔ `photo.txt`) for AI-training workflows; the Text tab loads it, auto-saves as you type (debounced), deletes the file when you empty it, and the caption follows its image through renames and moves
- **Video Support**: MP4, MOV and M4V clips appear as thumbnails and play in the preview pane with a scrubbable timeline, play/pause and volume — decoded natively by AVFoundation, so no FFmpeg install is required
- **Quick Tagging (0-9)**: Select photos and press a digit to badge them (top-right corner). One tag per photo — the same digit again clears it, a different digit replaces it. Tags are saved per directory in a `.photonize-tags.json` sidecar and restored when the folder is reloaded; because they're keyed by filename, a folder can be moved or copied and keep its tags. The file format is identical to the Windows client, so a folder tagged on one platform reads correctly on the other. The right-click **Tags** submenu offers **Move tagged images into sub-folders** (each tag's photos and their `.txt` captions moved into a named sub-folder, badges carried across), **Sort by tag** (folders first, then ascending digit, then untagged), and **Clear tags**. Tags follow their photos through an Apply Rename.
- **Settings Persistence**: Remembers your last directory, prefix, thumbnail size, and preferences
- **Two-Pass Rename Algorithm**: Safely renames files without conflicts, even when reordering

## System Requirements

- macOS 13.0 (Ventura) or later
- Universal binary — runs natively on both Apple Silicon (arm64) and Intel (x86_64) Macs

## Supported Formats

**Images:** JPG, JPEG, PNG, BMP, GIF, TIFF, WebP, HEIC, HEIF

**Raw:** Adobe DNG — shown from the JPEG preview embedded in the file, so both
uncompressed and compressed raw appear. DNGs can be browsed, reordered, tagged and
renamed, but not exported.

**Videos:** MP4, MOV, M4V — decoded natively by AVFoundation, no FFmpeg required

## Installation

### From Release

1. Download the latest `Photonize-{version}-universal.dmg` from [Releases](https://github.com/benstaniford/photonize/releases)
2. Open the DMG file
3. Drag `Photonize.app` to your Applications folder
4. Launch Photonize from Applications
5. The app is not yet notarized, so on first launch Gatekeeper will block it. **Right-click (or Control-click) `Photonize.app` in Applications and choose "Open"**, then confirm in the dialog. macOS remembers the choice, so subsequent launches are normal. (Recent macOS releases no longer reliably show an "Open Anyway" button in System Settings, so the right-click route is the dependable one.)

### Building from Source

#### Prerequisites

- macOS 13.0 or later
- Xcode 15.0 or later
- Command Line Tools for Xcode
- [XcodeGen](https://github.com/yonaskolb/XcodeGen) (`brew install xcodegen`) — the Xcode
  project is generated from `project.yml`, so regenerate it after editing that file

#### Build Steps

1. **Clone the repository**:
   ```bash
   git clone https://github.com/benstaniford/photonize.git
   cd photonize/macos
   ```

2. **(Re)generate and open in Xcode**:
   ```bash
   xcodegen generate   # only needed after changing project.yml
   open Photonize.xcodeproj
   ```

3. **Build and Run**:
   - Select the "Photonize" scheme in Xcode
   - Choose your target Mac (My Mac)
   - Press `Cmd+R` to build and run
   - Or press `Cmd+B` to build only

4. **Create a DMG** (optional):
   ```bash
   # After building in Xcode
   cd build/Build/Products/Release

   # Create DMG
   mkdir -p dmg-contents
   cp -R Photonize.app dmg-contents/
   ln -s /Applications dmg-contents/Applications

   hdiutil create -volname "Photonize" \
     -srcfolder dmg-contents \
     -ov -format UDZO \
     Photonize.dmg
   ```

## Usage

### Basic Workflow

1. **Open a Directory**:
   - Click "Browse..." to select a folder containing photos
   - Or use `Cmd+O` keyboard shortcut
   - The app will load all images and subfolders

2. **Organize Photos**:
   - Photos appear as thumbnails in a grid
   - Click and drag photos to reorder them
   - Use the thumbnail size slider to adjust preview size
   - Double-click folders to navigate into them
   - Double-click photos to open in default image viewer

3. **Rename Photos**:
   - Enter a prefix in the "Rename Prefix" field (e.g., `vacation`)
   - The app suggests a common prefix based on existing filenames
   - Click "Apply Rename" to rename all photos in display order
   - Files will be renamed as: `vacation-00001.jpg`, `vacation-00002.jpg`, etc.

4. **Preview & Caption Images**:
   - Click the toggle button (sidebar icon) to show/hide the inspector pane
   - Use the **Preview** / **Text** tabs at the top of the inspector to switch between the full-size preview and the prompt editor; the tab you last used is remembered
   - Preview tab shows image details (name, type, dimensions, file size)
   - Text tab edits the image's `.txt` caption: type and it saves automatically, and clearing it removes the file. Emptying the caption deletes the `.txt`; folders and videos have no caption and show a disabled message

### Keyboard Shortcuts

- `Cmd+O` - Open folder
- `Cmd+R` - Refresh current folder
- `Cmd+A` - Select all photos (folders excluded)
- `Cmd+Click` - Toggle selection of individual photos
- `Shift+Click` - Select the range from the last-clicked photo
- `Arrow keys` - Move the selection, wrapping at row edges
- `Shift+Arrow` - Extend the selection from the anchor
- `Return` - Open the focused photo, or enter the focused folder
- `Delete` or `Backspace` - Move selected photos to Trash
- `0`-`9` - Quick-tag the selection (same digit again clears it). Only when the grid has focus — digits typed in the path field or caption editor go to the text

### Context Menu

Right-click on any photo to access:
- **Open** - Open the photo in default image viewer
- **Show in Finder** - Reveal the file in Finder
- **Tags** - Move tagged images into sub-folders, sort by tag, or clear tags (enabled once something is tagged)
- **Delete** - Move the photo to Trash

### Opening From Finder and the Command Line

Photonize doesn't have to be launched from the Dock. Any of these open it straight
into the folder you care about, reusing the existing window rather than opening a
second one:

- **`open`** — `open -a Photonize ~/Pictures/holiday` navigates to that folder.
- **Dock / Finder drag** — drop a folder onto the Dock icon or onto the app in
  Finder.
- **Open With** — right-click a folder in Finder → **Open With** → **Photonize**.
- **Command-line flags** (via `--args`):
  - `open -a Photonize --args -d <folder>` (or `--directory`) opens that folder
    *instead of* restoring your last-used one — but keeps your thumbnail size,
    prefix and pane layout.
  - `open -a Photonize --args -e <file…>` (or `--export-webp`) exports the given
    images straight to WebP and then reveals the output folder.

  Unknown arguments are ignored, so running under the Xcode debugger (which injects
  flags such as `-NSDocumentRevisionsDebugMode YES`) is harmless.

The folder you open is remembered and reopens on the next launch. Photonize is not
sandboxed (it ships outside the Mac App Store), so it can read any folder you point
it at directly — macOS may still show a one-time privacy prompt the first time it
touches a protected location such as Desktop, Documents or Downloads.

### Finder Services

Once Photonize has been launched from `/Applications` at least once, two entries
appear in Finder's right-click **Services** submenu:

- **Open in Photonize** — on a folder, navigates Photonize to it.
- **Export to WebP** — on one or more images, exports them all to a `WebP`
  subfolder. Because Finder hands the whole selection to a single app instance,
  multi-file export works in one pass — an improvement over the Windows Explorer
  verb, which launches one app instance per selected file.

If the Services entries don't appear:

- Log out and back in, or run
  `/System/Library/CoreServices/pbs -flush` in Terminal, to make Launch Services
  re-scan. Services are only registered once the app is seen in `/Applications`.
- On some macOS versions Services are off by default. Enable them under
  **System Settings → Keyboard → Keyboard Shortcuts → Services**.

## Architecture

The app follows a strict MVVM (Model-View-ViewModel) architecture:

### Models
- `PhotoItem.swift` - Data model for photos and folders
- `AppSettings.swift` - Persistent settings structure

### ViewModels
- `PhotoGridViewModel.swift` - Central state management and business logic

### Views
- `ContentView.swift` - Main application layout with toolbar, status bar, and the tabbed inspector (`InspectorPane`)
- `PhotoGridView.swift` - Photo grid with thumbnails
- `PreviewPane.swift` - Image preview, video playback surface, and file information panel (the inspector's Preview tab)
- `PromptEditorPane.swift` - Monospaced `.txt` caption editor (the inspector's Text tab)

### Services
- `FileRenamer.swift` - Two-pass atomic rename algorithm (carries each photo's `.txt` caption across the rename)
- `PromptFileStore.swift` - Canonical `.txt` sidecar path plus load / save / delete-on-empty
- `TagService.swift` - `.photonize-tags.json` sidecar load/save (filename-keyed, 0-9, deleted when empty; the shared cross-client format)
- `TagSorter.swift` - Pure "sort by tag" ordering, free of file I/O and SwiftUI
- `ThumbnailGenerator.swift` - Async image loading, video frame extraction (AVFoundation), and thumbnail generation
- `SettingsManager.swift` - UserDefaults persistence

### Two-Pass Rename Algorithm

The rename algorithm prevents file conflicts when reordering:

1. **First Pass**: Rename all files to temporary names (`.tmp_rename` suffix)
2. **Second Pass**: Rename from temporary names to final pattern `{prefix}-{number:D5}{extension}`

This ensures that target filenames don't conflict with source filenames during the rename process.

## Project Structure

```
macos/
├── Photonize/
│   ├── PhotonizeApp.swift          # App entry point
│   ├── Models/
│   │   └── PhotoItem.swift         # Data models
│   ├── ViewModels/
│   │   └── PhotoGridViewModel.swift # State management
│   ├── Views/
│   │   ├── ContentView.swift       # Main UI + tabbed inspector
│   │   ├── PhotoGridView.swift     # Photo grid + tag badge + Tags menu
│   │   ├── PreviewPane.swift       # Preview tab
│   │   ├── PromptEditorPane.swift  # Text (caption) tab
│   │   └── TagFolderSheet.swift    # Move-tagged-to-sub-folders sheet
│   ├── Services/
│   │   ├── FileRenamer.swift       # Rename logic
│   │   ├── PromptFileStore.swift   # .txt caption load/save/delete
│   │   ├── TagService.swift        # .photonize-tags.json sidecar
│   │   ├── TagSorter.swift         # Pure sort-by-tag ordering
│   │   ├── ThumbnailGenerator.swift # Image loading
│   │   └── SettingsManager.swift   # Settings persistence
│   ├── Info.plist                  # App metadata
│   └── Photonize.entitlements      # Empty (not sandboxed); hardened runtime via build settings
├── Photonize.xcodeproj/
│   └── project.pbxproj             # Xcode project file
└── README.md                        # This file
```

## GitHub Actions

The project includes automated build workflows:

### Build Workflow
- **File**: `.github/workflows/build-macos.yml`
- **Trigger**: Manual workflow dispatch
- **Purpose**: Run the test suite and build a universal DMG for testing
- **Output**: Artifact available for 30 days

To trigger a build:
1. Go to Actions tab on GitHub
2. Select "Build macOS App"
3. Click "Run workflow"
4. Enter version (e.g., `v0.1.0` or `dev-build`)
5. Download artifact from workflow run

### Release Workflow
- **File**: `.github/workflows/release.yml` (shared with Windows — there is no separate macOS release workflow)
- **Trigger**: Git tag push (`v*.*.*`) or manual dispatch
- **Purpose**: Build and publish a single GitHub release carrying both platforms
- **Output**: The Windows MSI and the universal macOS DMG attached to one release

A macOS release is not cut separately. Pushing a `v*` tag builds and ships **both**
the Windows and macOS artifacts to the same GitHub release, with notes shared from
`CHANGELOG.md`'s `[Unreleased]` section:

```bash
git tag v0.1.0
git push origin v0.1.0
```

The macOS half of the workflow will:
1. Run `xcodebuild test` (the release is blocked if any test fails)
2. Build the app as a universal binary (arm64 + x86_64)
3. Create a DMG installer (`Photonize-{version}-universal.dmg`)
4. Attach the DMG to the shared GitHub release

## Troubleshooting

### App won't open (Gatekeeper)
1. Go to System Preferences > Security & Privacy
2. Click "Open Anyway" next to the blocked app message
3. Or right-click the app and select "Open"

### Photos not loading
- Check that the directory contains supported image or video formats
- Ensure you have read permissions for the directory
- Try refreshing with `Cmd+R`

### Can't rename files
- Ensure you have write permissions for the directory
- Check that no files are open in other applications
- Verify the prefix doesn't contain invalid characters (`:`, `/`, `\`)

### Preview pane not showing images
- Large images may take time to load
- HEIC/HEIF images require macOS codecs (built-in on macOS 11+)
- Check Console.app for any error messages

## Development

### Adding New Features

The SwiftUI architecture makes it easy to add features:

1. **Add UI**: Create new views in `Views/`
2. **Add Logic**: Extend `PhotoGridViewModel` with new properties/methods
3. **Add Services**: Create new service classes in `Services/`
4. **Persist State**: Update `AppSettings` model and `SettingsManager`

### Code Style

- Use SwiftUI declarative syntax
- Follow MVVM pattern strictly
- Keep views small and focused
- Use async/await for background work
- Document complex logic with comments

## Contributing

Contributions are welcome! Please:

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Test thoroughly on macOS
5. Submit a pull request

## License

See the main repository LICENSE file.

## Comparison with Windows Version

| Feature | Windows (WPF) | macOS (SwiftUI) |
|---------|---------------|-----------------|
| Photo Grid | ✓ | ✓ |
| Drag-Drop Reorder | ✓ | ✓ |
| Import from Finder | ✓ | ✓ |
| Drag into Folders | ✓ | ✓ |
| Batch Rename | ✓ | ✓ |
| Preview Pane | ✓ | ✓ |
| Prompt/Caption Editor (`.txt`) | ✓ | ✓ |
| Caption follows image on rename | ✗ | ✓ |
| Folder Navigation | ✓ | ✓ |
| Settings Persistence | ✓ | ✓ |
| WebP Export | ✓ | ✓ |
| PNG/JPG Export | ✓ | ✓ |
| Image Compare | ✓ | ✓ |
| Quick Tagging (0-9) | ✓ | ✓ |
| Tags sidecar (`.photonize-tags.json`) | ✓ | ✓ (shared format) |
| Open folder from shell / Finder | ✓ (`-d`, Explorer verb) | ✓ (`-d`, `open`, Dock, Service) |
| Export to WebP from Finder | ✓ (one instance per file) | ✓ (one instance, whole selection) |

## Credits

macOS version created as a Swift/SwiftUI port of the Windows WPF application.

Original Windows version: [Photonize](https://github.com/benstaniford/photonize)
