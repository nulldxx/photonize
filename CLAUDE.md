# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Repository Overview

Photo Organizer is a Windows desktop application built with WPF and .NET 8 that allows users to visually organize and batch-rename photographs. The application uses a strict MVVM architecture with drag-and-drop reordering capabilities.

## Common Development Commands

### Building and Running
```bash
# Build the solution
cd PhotoOrganizer
dotnet restore
dotnet build -c Release

# Run the application
dotnet run

# Publish standalone executable
dotnet publish -c Release -r win-x64 --self-contained
# Output: PhotoOrganizer/bin/Release/net8.0-windows/win-x64/publish/PhotoOrganizer.exe
```

### Visual Studio
```bash
# Open the solution
PhotoOrganizer.sln
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
- `SettingsService` - JSON persistence to `%APPDATA%/PhotoOrganizer/settings.json`

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
- `CommunityToolkit.Mvvm` (8.2.2) - MVVM helpers (though custom RelayCommand is currently used)
- `gong-wpf-dragdrop` (3.2.1) - Drag-and-drop framework for ItemsControl
- `Newtonsoft.Json` (13.0.3) - Settings serialization

## Supported Image Formats
JPG, JPEG, PNG, BMP, GIF, TIFF - defined in `ThumbnailGenerator.SupportedExtensions`

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
