# Changelog

All notable changes to Photonize will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [Unreleased]

### Added
- **Folder Deletion**: Right-click on folders to delete them and all their contents recursively with confirmation
- **Overwrite Options**: When exporting to formats that already exist, choose to overwrite all, skip existing, or cancel the operation
- **Keyboard Selection**: Press Ctrl+A to select all files (excludes folders to prevent accidental operations)
- **Auto-refresh After Export**: View automatically refreshes after export operations to show newly created subfolders

### Changed
- **Parallel Thumbnail Loading**: Dramatically improved performance with worker queue that loads thumbnails in parallel based on CPU core count
- **Retry Logic for Topaz Photo AI**: Added automatic retry with delays for transient errors during upscaling operations
- **Better Cancellation Handling**: UI remains responsive during long operations and properly handles user cancellation

### Fixed
- **Application Shutdown**: Fixed UI blocking during shutdown when background thumbnail loading is active
- **Unsaved Changes State**: Deleting folders no longer incorrectly enables the "Apply" button since folders don't affect rename operations

## [0.1.4] - Previous Release

_(Previous release notes would go here)_
