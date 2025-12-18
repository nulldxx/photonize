# Photonize macOS - Setup Guide

This document explains how to complete the setup of the Photonize macOS application after the initial code generation.

## What Has Been Created

A complete SwiftUI macOS application with the following structure:

```
macos/
├── Photonize/
│   ├── PhotonizeApp.swift                    # ✓ App entry point
│   ├── Models/
│   │   └── PhotoItem.swift                   # ✓ Data models
│   ├── ViewModels/
│   │   └── PhotoGridViewModel.swift          # ✓ State management
│   ├── Views/
│   │   ├── ContentView.swift                 # ✓ Main UI
│   │   ├── PhotoGridView.swift               # ✓ Photo grid
│   │   └── PreviewPane.swift                 # ✓ Preview panel
│   ├── Services/
│   │   ├── FileRenamer.swift                 # ✓ Two-pass rename algorithm
│   │   ├── ThumbnailGenerator.swift          # ✓ Image loading
│   │   └── SettingsManager.swift             # ✓ Settings persistence
│   ├── Info.plist                            # ✓ App metadata
│   └── Photonize.entitlements                # ✓ Sandbox permissions
├── Photonize.xcodeproj/
│   ├── project.pbxproj                       # ✓ Xcode project file
│   └── xcshareddata/xcschemes/
│       └── Photonize.xcscheme                # ✓ Build scheme
├── README.md                                  # ✓ Documentation
├── SETUP.md                                   # ✓ This file
└── create-xcode-project.sh                    # ✓ Setup script
```

GitHub Actions workflows:
- `.github/workflows/build-macos.yml` - Build DMG on demand
- `.github/workflows/release-macos.yml` - Create releases with tags

## Features Implemented

✅ Photo thumbnail grid with async loading
✅ Drag-and-drop reordering (via selection)
✅ Batch rename with two-pass algorithm
✅ Folder navigation (including parent directory)
✅ Preview pane with full-size images
✅ Settings persistence (UserDefaults)
✅ Delete photos (to Trash)
✅ Smart prefix detection
✅ Keyboard shortcuts (Cmd+O, Cmd+R)
✅ Context menus (Open, Show in Finder, Delete)
✅ Status bar with loading indicator
✅ Adjustable thumbnail size

## Next Steps

### 1. Open the Project in Xcode (on macOS)

```bash
cd ~/Code/photonize/macos
open Photonize.xcodeproj
```

### 2. Configure Build Settings (if needed)

In Xcode:
1. Select the Photonize project in the navigator
2. Select the Photonize target
3. Go to "Signing & Capabilities" tab
4. Select your development team
5. Verify bundle identifier: `com.photonize.macos`

### 3. Build and Run

Press `Cmd+R` to build and run the app.

The app should launch and you'll be able to:
- Browse directories
- View photo thumbnails
- Reorder photos by selection
- Rename photos with a prefix
- View previews
- Delete photos

### 4. Test the Build

Try building from command line (same as GitHub Actions will do):

```bash
cd ~/Code/photonize/macos
xcodebuild -project Photonize.xcodeproj \
  -scheme Photonize \
  -configuration Release \
  -arch x86_64 \
  -derivedDataPath build \
  build
```

The built app will be at: `build/Build/Products/Release/Photonize.app`

### 5. Create a DMG (Optional)

```bash
cd ~/Code/photonize/macos

# Copy built app
cp -R build/Build/Products/Release/Photonize.app ./

# Create DMG
mkdir -p dmg-contents
cp -R Photonize.app dmg-contents/
ln -s /Applications dmg-contents/Applications

hdiutil create -volname "Photonize" \
  -srcfolder dmg-contents \
  -ov -format UDZO \
  Photonize.dmg
```

### 6. Test GitHub Actions Build

Push the code to GitHub and trigger a build:

```bash
cd ~/Code/photonize
git add macos/
git add .github/workflows/build-macos.yml
git add .github/workflows/release-macos.yml
git commit -m "Add macOS version of Photonize"
git push
```

Then on GitHub:
1. Go to Actions tab
2. Select "Build macOS App"
3. Click "Run workflow"
4. Enter version: `v0.1.0`
5. Click "Run workflow"

The workflow will build the DMG and upload it as an artifact.

### 7. Create a Release

When ready to release:

```bash
git tag macos-v0.1.0
git push origin macos-v0.1.0
```

GitHub Actions will automatically:
1. Build the app
2. Create a DMG
3. Create a GitHub release
4. Attach the DMG to the release

## Known Limitations

The current implementation has some differences from the Windows version:

⚠️ **Not Yet Implemented:**
- WebP export (macOS doesn't have built-in WebP support)
- PNG/JPG export
- Image comparison view
- Photo import/copy
- Actual drag-and-drop reordering (SwiftUI LazyVGrid limitation)
  - Currently using selection-based reordering
  - True drag-drop would require custom NSCollectionView

⚠️ **Platform Differences:**
- File operations use macOS Trash instead of permanent delete
- Uses NSOpenPanel instead of FolderBrowserDialog
- Uses UserDefaults instead of JSON settings file
- Different keyboard shortcuts (Cmd instead of Ctrl)

## Future Enhancements

Possible improvements:

1. **Apple Silicon Support**: Add arm64 architecture to build
2. **Drag-Drop Reordering**: Implement custom collection view for true drag-drop
3. **Export Features**: Add WebP/PNG/JPG export (would need third-party library for WebP)
4. **Image Comparison**: Port the comparison window from Windows version
5. **Photo Import**: Add import/copy functionality
6. **Thumbnails Cache**: Cache thumbnails to disk for faster loading
7. **Multi-selection**: Improve multi-selection UI with shift-click ranges
8. **App Icon**: Create a custom app icon
9. **Dark Mode**: Add explicit dark mode support (currently uses system theme)
10. **Localization**: Add support for multiple languages

## Troubleshooting

### Build Fails

**Issue**: Xcode can't find source files
**Solution**: Make sure all Swift files are added to the target
1. Select file in navigator
2. Show File Inspector (right panel)
3. Check "Photonize" under Target Membership

**Issue**: Code signing errors
**Solution**:
1. Select your development team in Signing & Capabilities
2. Or disable signing for development: Build Settings > Code Signing Identity > Sign to Run Locally

### Runtime Issues

**Issue**: App crashes on launch
**Solution**: Check Console.app for crash logs, verify all file paths in project.pbxproj

**Issue**: Photos don't load
**Solution**: Ensure app has permission to read files (sandboxing should prompt automatically)

**Issue**: Can't rename files
**Solution**: Check that you have write permissions, verify sandbox entitlements include file write access

## Getting Help

1. Check the README.md for usage instructions
2. Look at the Windows version for reference implementation
3. Review SwiftUI documentation for UI issues
4. Check GitHub Issues for known problems

## Contributing

If you find bugs or want to add features:

1. Create an issue describing the problem/feature
2. Fork the repository
3. Create a feature branch
4. Make changes and test thoroughly
5. Submit a pull request

---

**Note**: This macOS version was created to match the Windows version's functionality. Some features may work differently due to platform differences, but the core workflow (browse, reorder, rename) is identical.
