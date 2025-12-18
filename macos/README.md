# Photonize for macOS

A macOS desktop application for visually organizing and batch-renaming photographs. Built with SwiftUI for a native macOS experience.

## Features

- **Visual Organization**: Browse photos with thumbnail previews and folder navigation
- **Drag-and-Drop Reordering**: Reorder photos by dragging them into your desired sequence
- **Batch Rename**: Rename multiple files at once with customizable prefix (e.g., `vacation-00001`, `vacation-00002`)
- **Smart Prefix Detection**: Automatically detects common filename patterns from existing files
- **Preview Pane**: View full-size image previews with detailed file information
- **Settings Persistence**: Remembers your last directory, prefix, thumbnail size, and preferences
- **Two-Pass Rename Algorithm**: Safely renames files without conflicts, even when reordering

## System Requirements

- macOS 13.0 (Ventura) or later
- Intel-based Mac (Apple Silicon support coming soon)

## Supported Image Formats

JPG, JPEG, PNG, BMP, GIF, TIFF, WebP, HEIC, HEIF

## Installation

### From Release

1. Download the latest `Photonize-{version}-intel.dmg` from [Releases](https://github.com/benstaniford/photonize/releases)
2. Open the DMG file
3. Drag `Photonize.app` to your Applications folder
4. Launch Photonize from Applications
5. If macOS blocks the app (Gatekeeper), go to System Preferences > Security & Privacy and click "Open Anyway"

### Building from Source

#### Prerequisites

- macOS 13.0 or later
- Xcode 15.0 or later
- Command Line Tools for Xcode

#### Build Steps

1. **Clone the repository**:
   ```bash
   git clone https://github.com/benstaniford/photonize.git
   cd photonize/macos
   ```

2. **Open in Xcode**:
   ```bash
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

4. **Preview Images**:
   - Click the preview toggle button (sidebar icon) to show/hide the preview pane
   - Select a photo to see its full-size preview
   - Preview pane shows image details (name, type, dimensions, file size)

### Keyboard Shortcuts

- `Cmd+O` - Open folder
- `Cmd+R` - Refresh current folder
- `Cmd+Click` - Toggle selection of individual photos
- `Shift+Click` - Extend selection
- `Delete` or `Backspace` - Move selected photos to Trash

### Context Menu

Right-click on any photo to access:
- **Open** - Open the photo in default image viewer
- **Show in Finder** - Reveal the file in Finder
- **Delete** - Move the photo to Trash

## Architecture

The app follows a strict MVVM (Model-View-ViewModel) architecture:

### Models
- `PhotoItem.swift` - Data model for photos and folders
- `AppSettings.swift` - Persistent settings structure

### ViewModels
- `PhotoGridViewModel.swift` - Central state management and business logic

### Views
- `ContentView.swift` - Main application layout with toolbar and status bar
- `PhotoGridView.swift` - Photo grid with thumbnails
- `PreviewPane.swift` - Image preview and file information panel

### Services
- `FileRenamer.swift` - Two-pass atomic rename algorithm
- `ThumbnailGenerator.swift` - Async image loading and thumbnail generation
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
│   │   ├── ContentView.swift       # Main UI
│   │   ├── PhotoGridView.swift     # Photo grid
│   │   └── PreviewPane.swift       # Preview panel
│   ├── Services/
│   │   ├── FileRenamer.swift       # Rename logic
│   │   ├── ThumbnailGenerator.swift # Image loading
│   │   └── SettingsManager.swift   # Settings persistence
│   ├── Info.plist                  # App metadata
│   └── Photonize.entitlements      # Sandbox permissions
├── Photonize.xcodeproj/
│   └── project.pbxproj             # Xcode project file
└── README.md                        # This file
```

## GitHub Actions

The project includes automated build workflows:

### Build Workflow
- **File**: `.github/workflows/build-macos.yml`
- **Trigger**: Manual workflow dispatch
- **Purpose**: Build DMG for testing
- **Output**: Artifact available for 30 days

To trigger a build:
1. Go to Actions tab on GitHub
2. Select "Build macOS App"
3. Click "Run workflow"
4. Enter version (e.g., `v0.1.0` or `dev-build`)
5. Download artifact from workflow run

### Release Workflow
- **File**: `.github/workflows/release-macos.yml`
- **Trigger**: Git tag push (`macos-v*.*.*`) or manual dispatch
- **Purpose**: Build and create GitHub release
- **Output**: DMG attached to release

To create a release:
```bash
git tag macos-v0.1.0
git push origin macos-v0.1.0
```

The workflow will:
1. Build the app for Intel Macs
2. Create a DMG installer
3. Create a GitHub release with release notes
4. Attach the DMG to the release

## Troubleshooting

### App won't open (Gatekeeper)
1. Go to System Preferences > Security & Privacy
2. Click "Open Anyway" next to the blocked app message
3. Or right-click the app and select "Open"

### Photos not loading
- Check that the directory contains supported image formats
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
| Batch Rename | ✓ | ✓ |
| Preview Pane | ✓ | ✓ |
| Folder Navigation | ✓ | ✓ |
| Settings Persistence | ✓ | ✓ |
| WebP Export | ✓ | Planned |
| PNG/JPG Export | ✓ | Planned |
| Image Compare | ✓ | Planned |
| Photo Import | ✓ | Planned |

## Credits

macOS version created as a Swift/SwiftUI port of the Windows WPF application.

Original Windows version: [Photonize](https://github.com/benstaniford/photonize)
