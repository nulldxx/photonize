# Changelog

All notable changes to Photonize will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [Unreleased]

### Added
- **WebP Export**: Export selected photos to WebP format with configurable quality settings
- **Folder Navigation**: Browse folders directly within the app - double-click to enter folders, use ".." to go back to parent
- **Folder Thumbnails**: Subfolders now display with folder icons for easy identification
- **Drag-and-Drop to Folders**: Move photos into subfolders or parent directory by dragging and dropping them onto folder items
- **Icon-Based UI**: Replaced text buttons with icons for cleaner interface
  - Refresh button now uses circular arrow icon (↻)
  - Preview toggle uses eye icon (👁️)
  - Apply button uses checkmark icon (✓)
- **Smart Apply Button**: Apply button is now disabled when there are no pending changes to save
- **Auto-Load**: Photos automatically load when changing directory path or pressing Enter in directory textbox
- **WebP Support**: Application now recognizes and loads .webp image files

### Changed
- Updated SixLabors.ImageSharp to version 3.1.12 for improved image processing
- Updated all package references to latest stable versions
- Apply button text changed from "Apply Rename" to "✓ Apply" with visual state indicating pending changes
- Context menus no longer appear when right-clicking on folders (only on photos)
- Preview toggle button moved next to Thumbnail Size slider for better UI organization

### Fixed
- Improved folder filtering to prevent folders from being included in reordering operations
- Fixed context menu appearing inappropriately on folder items

## [0.1.4] - Previous Release

_(Previous release notes would go here)_
