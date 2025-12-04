# Photo Organizer

A Windows desktop application for organizing and renaming photographs with visual thumbnail preview and drag-and-drop reordering.

## Features

- **Visual thumbnail browser** - Browse photos in a grid with adjustable thumbnail sizes (100-400px)
- **Drag-and-drop reordering** - Rearrange photos by dragging thumbnails to change the rename order
- **Smart renaming** - Rename files following the pattern: `prefix-00001.jpg`, `prefix-00002.jpg`, etc.
- **Path selection** - Type or browse to select the directory containing your photos
- **Settings persistence** - Automatically remembers your last directory and preferences
- **Keyboard shortcuts** - F5 to refresh, Ctrl+O to browse
- **Safe rename operations** - Uses temporary files to avoid conflicts during batch rename

## Requirements

- Windows 10 or later
- .NET 8 Runtime (will be prompted to install if missing)

## Building from Source

### Prerequisites
- .NET 8 SDK
- Visual Studio 2022 (or VS Code with C# extension)

### Build Instructions

**Using Visual Studio:**
1. Open `Photonize.sln` in Visual Studio 2022
2. Press F5 to build and run

**Using command line:**
```bash
cd Photonize
dotnet restore
dotnet build -c Release
```

### Run the Application

```bash
cd Photonize
dotnet run
```

Or build and run the executable:

```bash
dotnet publish -c Release -r win-x64 --self-contained
```

The executable will be in: `Photonize/bin/Release/net8.0-windows/win-x64/publish/Photonize.exe`

### Building the Installer

To create a Windows Installer (.msi):

**Prerequisites:**
- Install WiX Toolset v3.11+ from https://wixtoolset.org/releases/

**Build Steps:**
1. Open `Photonize.sln` in Visual Studio 2022
2. Set configuration to **Release|x64**
3. Build the `Photonize.Installer` project
4. Find the installer at: `Photonize.Installer\bin\x64\Release\Photonize.msi`

See `Photonize.Installer/README.md` for detailed instructions.

## Usage

### Quick Start from Windows Explorer
After installation, you can right-click on any folder in Windows Explorer and select **"Open in Photo Organizer"** to quickly organize photos in that folder.

### Manual Usage

1. **Select Directory**
   - Type the path in the directory textbox, or
   - Click "Browse..." to select a folder containing photos, or
   - Right-click a folder in Windows Explorer and choose "Open in Photo Organizer"
   - Click "Load Photos" to load thumbnails

2. **Adjust Thumbnail Size**
   - Use the slider to resize thumbnails between 100-400px for comfortable viewing

3. **Reorder Photos**
   - Click and drag thumbnails to rearrange them in your desired order
   - The rename operation will use this order (left-to-right, top-to-bottom)

4. **Enter Rename Prefix**
   - Type a prefix in the "Rename Prefix" textbox (e.g., "vacation", "photos")
   - Files will be renamed to: `prefix-00001.jpg`, `prefix-00002.jpg`, etc.

5. **Apply Rename**
   - Click "Apply Rename" button
   - Confirm the operation in the dialog
   - Files are renamed safely on disk

### Command Line Usage

You can also launch Photo Organizer from the command line with a directory:

```bash
Photonize.exe -d "C:\Path\To\Photos"
# or
Photonize.exe --directory "C:\Path\To\Photos"
```

## Supported Image Formats

- JPEG (.jpg, .jpeg)
- PNG (.png)
- BMP (.bmp)
- GIF (.gif)
- TIFF (.tif, .tiff)

## Technical Details

### Architecture
- **Framework**: WPF with .NET 8
- **Pattern**: MVVM (Model-View-ViewModel)
- **Drag-and-Drop**: GongSolutions.Wpf.DragDrop library
- **Settings**: JSON file stored in `%APPDATA%/Photonize/settings.json`

### Project Structure
```
Photonize/
├── Models/              # Data models (PhotoItem)
├── ViewModels/          # Business logic (MainViewModel)
├── Services/            # Core services (ThumbnailGenerator, FileRenamer, SettingsService)
├── Helpers/             # Utility classes (PhotoDropHandler)
├── MainWindow.xaml      # Main UI layout
└── App.xaml             # Application entry point

Photonize.Installer/
├── Product.wxs          # WiX installer definition
├── License.rtf          # License agreement
└── Photonize.ico   # Application icon
```

### Rename Logic

The rename algorithm (ported from the Python `auto-rename` script) works as follows:

1. Photos are sorted by their `DisplayOrder` (set by drag-and-drop position)
2. Each photo is assigned a new name: `{prefix}-{number:05d}{extension}`
3. A two-pass rename strategy prevents file conflicts:
   - First pass: Rename all files to temporary names
   - Second pass: Rename from temporary names to final names
4. All operations are atomic to prevent data loss

## Keyboard Shortcuts

- **F5** - Refresh/reload photos from current directory
- **Ctrl+O** - Open browse directory dialog

## Windows Explorer Integration

The installer adds context menu integration to Windows Explorer:

- **Right-click on a folder** → "Open in Photo Organizer" - Opens that folder
- **Right-click on folder background** → "Open in Photo Organizer" - Opens the current folder

This feature can be enabled/disabled during installation via the "Windows Explorer Integration" feature.

## Settings

Settings are automatically saved to: `%APPDATA%/Photonize/settings.json`

Persisted settings include:
- Last opened directory path
- Last used thumbnail size
- Last used rename prefix

## Troubleshooting

### Photos not loading
- Check that the directory exists and is accessible
- Ensure the directory contains supported image formats
- Check file permissions

### Rename fails
- Ensure no files are open in other applications
- Check that you have write permissions in the directory
- Verify the prefix doesn't contain invalid filename characters
- Make sure target filenames don't conflict with existing files outside the photo set

### Drag-and-drop not working
- Try closing and reopening the application
- Ensure you're dragging from one thumbnail to another position

## License

See LICENSE file in the repository root.

## Similar Projects

This application's rename functionality is based on the `auto-rename` Python script located at `~/dot-files/scripts/auto-rename`, adapted for interactive use with a graphical interface.
