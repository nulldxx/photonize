# Photonize

**Automatically order and rename large photo collections, no databases, just filesystem**

Photonize is a Windows desktop application that makes it simple to organize hundreds or thousands of photos. Visually reorder your photos by dragging thumbnails, then automatically rename them all at once with sequential numbering. Perfect for photographers, digital archivists, or anyone managing large photo collections.

Grab the latest windows installer <a href="https://github.com/benstaniford/photonize/releases">here</a>.

<img width="1672" height="1197" alt="image" src="https://github.com/user-attachments/assets/97ae089f-5f9c-40ae-8f1c-5986911eec5e" />

## Why Photonize?

When you have hundreds of photos from a vacation, event, or photo shoot, getting them organized can be tedious. Photonize solves this by letting you:

- **See all your photos at once** - Browse thumbnails of entire folders
- **Drag to reorder** - Visually arrange photos in the order you want
- **Rename in one click** - Automatically rename all photos with sequential numbering (vacation-00001.jpg, vacation-00002.jpg, etc.)
- **Work quickly** - Process large photo sets in minutes, not hours

## Features

- 📸 **Visual Organization** - See all photos as thumbnails and drag them to reorder
- 📁 **Folder Navigation** - Browse subfolders directly in the app, double-click to navigate, drag photos into folders
- 🔄 **Batch Rename** - Rename entire photo collections automatically with sequential numbers
- 🖼️ **Preview Pane** - View full-size previews of selected photos
- ⚖️ **Image Compare** - Select two photos and compare them side-by-side with an interactive A/B slider
- 🌐 **WebP Export** - Export photos to WebP format for efficient web publishing
- 💾 **Safe Operations** - Smart renaming algorithm prevents file conflicts and data loss
- 🎯 **Auto-Detection** - Automatically detects existing naming patterns
- 🔍 **Explorer Integration** - Right-click folders in Windows Explorer to open directly
- 🌙 **Dark Theme** - Easy-on-the-eyes dark interface
- 📏 **Adjustable Thumbnails** - Resize thumbnails from 100px to 400px

## Download & Install

### System Requirements

- **Operating System**: Windows 10 or later (64-bit)
- **.NET Runtime**: .NET 8.0 Desktop Runtime - [Download here](https://dotnet.microsoft.com/download/dotnet/8.0)
- **Disk Space**: ~50 MB

### Installation

1. Download the latest installer from the [Releases page](https://github.com/yourusername/photonize/releases)
2. Run `PhotonizeInstaller-vX.X.X-x64.msi`
3. Follow the installation wizard
4. Launch Photonize from the Start Menu or Desktop shortcut

The installer includes:
- Desktop shortcut (optional)
- Start Menu shortcut
- Windows Explorer context menu integration (optional)

## Quick Start

### Method 1: Using Windows Explorer (Fastest)

**For organizing photos:**
1. Right-click any folder containing photos in Windows Explorer
2. Select **"Open in Photonize"**
3. Photos load automatically

**For quick WebP export:**
1. Select one or more image files in Windows Explorer
2. Right-click and select **"Export to WebP"**
3. Photonize opens and immediately converts selected files to WebP format
4. Output saved to "WebP" subfolder in the same location

### Method 2: Using the Application

1. Launch Photonize
2. Click **"Browse..."** or type a folder path and press Enter
3. Photos load automatically

### Organizing and Renaming

1. **Navigate Folders**
   - Folders appear at the top with folder icons
   - Double-click any folder to navigate into it
   - Click the ".." folder to go back to the parent directory
   - Drag photos onto folders to move them

2. **Reorder Photos**
   - Drag and drop thumbnails to arrange them in your desired sequence
   - Order is left-to-right, top-to-bottom

3. **Set a Prefix**
   - Type a prefix in the "Rename Prefix" box (e.g., "vacation", "wedding", "photos")
   - Photonize auto-detects common prefixes if files are already named

4. **Adjust Thumbnail Size** (Optional)
   - Use the slider to make thumbnails larger or smaller for easier viewing

5. **Preview Photos** (Optional)
   - Click the eye icon (👁️) next to the thumbnail slider to open a preview pane
   - Double-click any photo to open it in your default image viewer

6. **Export to WebP** (Optional)
   - Right-click photos and select "Export WebP"
   - Creates optimized WebP versions in a "webp" subfolder

7. **Compare Images** (Optional)
   - Select exactly two photos (use Ctrl+Click to select multiple)
   - Right-click and select "Image Compare"
   - A new window opens showing both images overlaid
   - Hover your mouse over the image to reveal an interactive vertical divider
   - Move your mouse left and right to compare the images side-by-side
   - Press ESC or click "✕ Close" to exit the comparison view

8. **Apply Rename**
   - Click **"✓ Apply"** (enabled when you have pending changes)
   - Confirm the operation
   - All photos are renamed: `prefix-00001.jpg`, `prefix-00002.jpg`, `prefix-00003.jpg`, etc.

## Supported Image Formats

- JPEG (.jpg, .jpeg)
- PNG (.png)
- WebP (.webp)
- BMP (.bmp)
- GIF (.gif)
- TIFF (.tif, .tiff)

## Keyboard Shortcuts

- **F5** - Refresh photos from current directory
- **Ctrl+O** - Browse for a new directory
- **Delete** - Delete selected photo(s)

## Tips & Tricks

### Comparing Similar Photos

- Use Image Compare to choose between nearly identical shots
- Perfect for comparing different exposures, filters, or edits of the same scene
- Select two photos, right-click, choose "Image Compare"
- Move your mouse across the image to reveal differences
- Great for before/after comparisons or picking the best shot from a burst

### Working with Large Photo Sets

- Start with smaller thumbnail sizes to see more photos at once
- Use the auto-detected prefix if your files are already partially organized
- The preview pane can be resized by dragging the divider

### Organizing Photos into Folders

- Create subfolders for different categories or events
- Drag photos onto folder icons to move them into that folder
- Drag photos onto the ".." folder to move them to the parent directory
- Navigate into folders to continue organizing within them

### Organizing by Date or Event

1. Sort photos by date in Windows Explorer first
2. Load them into Photonize
3. Fine-tune the order by dragging specific photos
4. Use a descriptive prefix like "2024-12-hawaii" or "wedding-ceremony"

### Multiple Photo Sessions

- Load one folder at a time
- Use different prefixes for different sessions (e.g., "day1", "day2")
- Create subfolders for each session and drag photos to organize them
- Photonize remembers your last directory and settings

## Troubleshooting

**Photos won't load?**
- Check that the folder contains supported image formats (JPG, PNG, WebP, BMP, GIF, TIFF)
- Verify you have permission to read the folder
- Press F5 or click the refresh icon (↻) to reload the current directory

**Can't rename files?**
- Close any programs that might have the photos open (image viewers, editors)
- Check that you have write permission for the folder
- Make sure your prefix doesn't contain invalid characters (< > : " / \ | ? *)

**Drag and drop not working?**
- Click on a photo thumbnail first to select it
- Drag from the middle of the thumbnail
- Try restarting the application if issues persist

**Preview pane shows "No image selected"?**
- Click on a photo thumbnail to select it
- The preview updates automatically when you select different photos

## Command Line Usage

You can launch Photonize with a specific folder:

```bash
Photonize.exe -d "C:\Path\To\Photos"
```

Or:

```bash
Photonize.exe --directory "C:\Path\To\Photos"
```

## Privacy & Data

- Photonize works entirely on your local computer
- No data is sent to the internet
- Settings are stored locally in `%APPDATA%\Photonize\settings.json`
- No telemetry or tracking

## Uninstalling

Use Windows "Add or Remove Programs":
1. Open Windows Settings
2. Go to Apps → Installed apps
3. Find "Photonize" and click Uninstall

## License

See LICENSE file for details.

## Support

For bug reports or feature requests, please visit the [Issues page](https://github.com/yourusername/photonize/issues).

---

**Note**: This application performs file rename operations. While it uses safe algorithms to prevent data loss, it's always a good idea to have backups of important photos.
