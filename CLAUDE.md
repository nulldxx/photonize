# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Repository Overview

Photonize is a Windows desktop application built with WPF and .NET 8 that allows users to visually organize and batch-rename photographs. The application uses a strict MVVM architecture with drag-and-drop reordering capabilities.

## Common Development Commands

### Building and Running
```bash
# Build the solution
cd Photonize
dotnet restore
dotnet build -c Release

# Run the application
dotnet run

# Publish standalone executable
dotnet publish -c Release -r win-x64 --self-contained
# Output: Photonize/bin/Release/net8.0-windows/win-x64/publish/Photonize.exe
```

### Visual Studio
```bash
# Open the solution
Photonize.sln
# Then press F5 to build and run
```

## Architecture

### MVVM Pattern
The application follows strict MVVM separation:
- **Models** (`Models/PhotoItem.cs`) - Data objects with INotifyPropertyChanged for UI binding
- **ViewModels** (`ViewModels/MainViewModel.cs`) - Business logic, commands, and state management
- **Views** (`MainWindow.xaml`) - XAML UI with data binding only, minimal code-behind

### Key Components

**MainViewModel** is the central orchestrator:
- Manages ObservableCollection<PhotoItem> for the photo grid
- Exposes ICommand properties (BrowseDirectory, LoadPhotos, ApplyRename, Refresh)
- Coordinates between services (ThumbnailGenerator, FileRenamer, SettingsService)
- Updates command CanExecute states when properties change via RelayCommand.RaiseCanExecuteChanged()

**Services Layer** (`Services/`):
- `ThumbnailGenerator` - Asynchronously loads images and creates BitmapImage thumbnails with caching
- `FileRenamer` - Implements two-pass atomic rename algorithm (ported from `~/dot-files/scripts/auto-rename`)
- `SettingsService` - JSON persistence to `%APPDATA%/Photonize/settings.json`

**Drag-and-Drop** (`Helpers/PhotoDropHandler.cs`):
- Implements `IDropTarget` from GongSolutions.Wpf.DragDrop
- Calls `MainViewModel.UpdateDisplayOrder()` after drop to update PhotoItem.DisplayOrder sequence
- This order determines final rename sequence

### Rename Algorithm
The FileRenamer service uses a critical two-pass strategy to avoid file conflicts:
1. **First pass**: Rename all files to temporary names (`.tmp_rename` suffix)
2. **Second pass**: Rename from temporary names to final pattern `{prefix}-{number:D5}{extension}`

This prevents issues when reordering files where target names might conflict with existing source names. The algorithm is based on the Python `auto-rename` script from `~/dot-files/scripts/auto-rename`.

### Data Binding
- Photos collection binds to ItemsControl with WrapPanel layout
- ThumbnailSize property (100-400) drives Image Width/Height via RelativeSource binding to Window DataContext
- All commands use CanExecute to enable/disable UI buttons based on state

## Key NuGet Dependencies
- `CommunityToolkit.Mvvm` (8.4.0) - MVVM helpers (though custom RelayCommand is currently used)
- `gong-wpf-dragdrop` (4.0.0) - Drag-and-drop framework for ItemsControl
- `Newtonsoft.Json` (13.0.4) - Settings serialization
- `SixLabors.ImageSharp` (3.1.12) - Image processing and WebP export

**IMPORTANT**: When adding new NuGet packages, you **must** also update the WiX installer (`Photonize.Installer/Product.wxs`) to include the new DLL files. See the "Installer Configuration" section below.

## Supported Image Formats
JPG, JPEG, PNG, WebP, BMP, GIF, TIFF - defined in `ThumbnailGenerator.SupportedExtensions`

## UI Theming
The application uses a comprehensive dark theme defined in `App.xaml`:
- **Color palette**: Charcoal/dark gray backgrounds (#1E1E1E, #2D2D30, #3E3E42) with light text (#F1F1F1)
- **Accent color**: Blue (#007ACC) for focus states, hover effects, and primary actions
- **Custom styles**: All controls (Window, TextBox, Button, GroupBox, Slider, etc.) have dark theme styles
- **Photo thumbnails**: Use `PhotoBorderStyle` with hover effects (border highlights on mouse-over)
- **Primary button**: "Apply Rename" uses `PrimaryButtonStyle` with accent blue background

When adding new UI elements, use existing resource brushes (`DarkBackgroundBrush`, `AccentBrush`, `TextPrimaryBrush`, etc.) to maintain consistency.

## Settings Persistence
AppSettings class with JSON serialization stores:
- LastDirectoryPath - Auto-loads on startup if directory still exists
- LastThumbnailSize - Restores slider position
- LastPrefix - Optional, for convenience

## Important Implementation Details

### Command Pattern
Commands use custom `RelayCommand` class with explicit `RaiseCanExecuteChanged()` calls. When adding new commands, ensure:
1. Property setters call `((RelayCommand)CommandName).RaiseCanExecuteChanged()` to update button states
2. CanExecute delegates reference current state (e.g., `() => Photos.Count > 0`)

### Async Loading
Thumbnail generation runs asynchronously to prevent UI blocking:
- `ThumbnailGenerator.GenerateThumbnailAsync()` uses `Task.Run()` with `BitmapImage.Freeze()` for cross-thread access
- MainViewModel sets `IsLoading` property to show/hide progress indicator in StatusBar

### File Safety
When modifying FileRenamer:
- Always validate prefix with `Path.GetInvalidFileNameChars()`
- Check for conflicts with existing files NOT in the rename set
- Maintain atomic two-pass rename to prevent data loss
- Handle exceptions at service layer and return tuple results `(bool Success, string Message)`

## Installer Configuration

### Adding NuGet Package Dependencies to the Installer

The WiX installer (`Photonize.Installer/Product.wxs`) manually lists each DLL file to include. When you add a new NuGet package, you **must** update the installer:

1. **Add a new Component** in the `DirectoryRef Id="INSTALLFOLDER"` section:
   ```xml
   <Component Id="YourPackageName" Guid="{NEW-GUID-HERE}">
     <File Id="YourPackageNameDll"
           Source="$(var.Photonize.TargetDir)YourPackage.dll"
           KeyPath="yes" />
   </Component>
   ```
   - Generate a new GUID using `uuidgen` or an online GUID generator
   - Use the exact DLL filename from the NuGet package

2. **Add a ComponentRef** to the `ProductFeature` feature:
   ```xml
   <Feature Id="ProductFeature" Title="Photonize" Level="1">
     <!-- ... existing components ... -->
     <ComponentRef Id="YourPackageName" />
   </Feature>
   ```

3. **Find DLL names** by checking the publish output:
   ```bash
   dotnet publish Photonize/Photonize.csproj -c Release -o temp
   ls temp/*.dll
   ```

### Common Issue: "Library not found" errors after installation

If the application works in Visual Studio but fails after installation with errors like "could not load library" or "assembly not found":

1. The installer is missing a required DLL
2. Check what DLLs are in the publish directory but not in Product.wxs
3. Add the missing Component and ComponentRef entries
4. Rebuild the installer

### Future Improvement: Automated File Harvesting

The current installer manually lists files. A better approach would be to use WiX Heat.exe to automatically harvest all files from the publish directory. This would prevent missing dependency issues but requires changes to the build process.

## Release Process

### Updating Release Notes

Before creating a new release, update the `CHANGELOG.md` file in the repository root:

1. **Add changes to the [Unreleased] section**:
   ```markdown
   ## [Unreleased]

   ### Added
   - New feature descriptions

   ### Changed
   - Changes to existing functionality

   ### Fixed
   - Bug fixes
   ```

2. **Follow Keep a Changelog format**:
   - Use categories: Added, Changed, Deprecated, Removed, Fixed, Security
   - Write clear, user-focused descriptions
   - Include specific feature names and behavior changes

3. **GitHub Action automatically uses CHANGELOG.md**:
   - The release workflow (`.github/workflows/release.yml`) extracts the `[Unreleased]` section
   - This content becomes the release notes on GitHub
   - Installation instructions and system requirements are automatically appended

### Creating a Release

1. **Ensure CHANGELOG.md is up to date** with all changes since the last release

2. **Create and push a version tag**:
   ```bash
   git tag v0.1.5
   git push origin v0.1.5
   ```

3. **GitHub Actions automatically**:
   - Builds the application
   - Creates MSI installer
   - Extracts release notes from CHANGELOG.md
   - Creates GitHub release with the MSI attached

4. **After release, archive the released changes**:
   - Move the `[Unreleased]` content to a new version section in CHANGELOG.md
   - Clear the `[Unreleased]` section for the next development cycle
   ```markdown
   ## [Unreleased]

   _(Empty - no unreleased changes)_

   ## [0.1.5] - 2025-12-05

   ### Added
   - Features that were released...
   ```

### Manual Release (if needed)

You can also trigger a release manually from GitHub Actions:
1. Go to Actions → Build and Release Photonize
2. Click "Run workflow"
3. Enter the version (e.g., `v0.1.5`)
4. Click "Run workflow"

### Release Checklist

Before tagging a release:
- [ ] All tests pass locally
- [ ] CHANGELOG.md `[Unreleased]` section is complete and accurate
- [ ] README.md reflects any new features or changes
- [ ] Version number follows semantic versioning (MAJOR.MINOR.PATCH)
- [ ] Build and test the MSI installer locally if possible
