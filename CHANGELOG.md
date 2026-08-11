# Changelog

All notable changes to Photonize will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [Unreleased]

### Added
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

## [0.1.23] - 2026-01-09

### Added
- **Video Support**: Full support for organizing and batch-renaming video files (MP4, MOV, M4V) alongside photos
  - Video thumbnails with proper aspect ratio preservation
  - Video preview with playback controls in the preview pane
  - Timeline scrubbing, play/pause, and volume controls
  - Video-specific context menu (without image-specific export options)
