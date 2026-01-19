# Changelog

All notable changes to Photonize will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [Unreleased]

### Added
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
