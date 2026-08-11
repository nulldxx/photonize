import Foundation
import AppKit
import ImageIO
import UniformTypeIdentifiers

class ThumbnailGenerator {
    private static let supportedExtensions = ["jpg", "jpeg", "png", "bmp", "gif", "tiff", "tif", "webp", "heic", "heif"]

    /// Shared across instances (PreviewPane spins up its own generator) and keyed
    /// by `path|mtime|size`: the mtime component re-renders an edited file, the
    /// size component gives each slider bucket its own entry. `NSCache` evicts
    /// under memory pressure on top of the count cap.
    private static let cache: NSCache<NSString, NSImage> = {
        let cache = NSCache<NSString, NSImage>()
        cache.countLimit = 400
        return cache
    }()

    func isImageFile(_ url: URL) -> Bool {
        let ext = url.pathExtension.lowercased()
        return Self.supportedExtensions.contains(ext)
    }

    func generateThumbnail(for url: URL, maxSize: Int) async -> NSImage? {
        // Keep the key a Swift `String` (Sendable) across the closure boundary and
        // bridge to `NSString` only at the cache calls.
        let key = Self.cacheKey(for: url, size: maxSize)
        if let cached = Self.cache.object(forKey: key as NSString) {
            return cached
        }

        return await withCheckedContinuation { continuation in
            DispatchQueue.global(qos: .userInitiated).async {
                let thumbnail = Self.decodeThumbnail(url: url, maxSize: maxSize)
                if let thumbnail {
                    Self.cache.setObject(thumbnail, forKey: key as NSString)
                }
                continuation.resume(returning: thumbnail)
            }
        }
    }

    func generatePreview(for url: URL) async -> NSImage? {
        return await withCheckedContinuation { continuation in
            DispatchQueue.global(qos: .userInitiated).async {
                continuation.resume(returning: Self.decodeOrientedFull(url: url))
            }
        }
    }

    /// System folder icon for `url` — one line, matches light/dark appearance and
    /// picks up any custom icon the folder has been given in Finder, which the old
    /// hand-drawn bezier art could not. Multi-representation, so it stays crisp at
    /// any display size without re-baking.
    func folderIcon(for url: URL, size: Int) -> NSImage {
        let icon = NSWorkspace.shared.icon(forFile: url.path)
        icon.size = NSSize(width: size, height: size)
        return icon
    }

    // MARK: - Decoding

    /// Downscaled decode via ImageIO. Never materialises the full image, reuses an
    /// embedded thumbnail when one is large enough, and applies EXIF orientation.
    private static func decodeThumbnail(url: URL, maxSize: Int) -> NSImage? {
        let opts: [CFString: Any] = [
            kCGImageSourceCreateThumbnailFromImageAlways: true,
            kCGImageSourceThumbnailMaxPixelSize: maxSize,
            kCGImageSourceCreateThumbnailWithTransform: true,   // honours EXIF orientation
            kCGImageSourceShouldCacheImmediately: true,
        ]
        guard let src = CGImageSourceCreateWithURL(url as CFURL, nil),
              let cg = CGImageSourceCreateThumbnailAtIndex(src, 0, opts as CFDictionary)
        else { return nil }
        return NSImage(cgImage: cg, size: .zero)
    }

    /// Full-resolution decode for the preview pane, with EXIF orientation applied.
    /// Uses ImageIO with the transform flag but no downscale so a sideways phone
    /// photo appears upright; falls back to `NSImage(contentsOf:)` if ImageIO
    /// cannot read the file.
    private static func decodeOrientedFull(url: URL) -> NSImage? {
        guard let src = CGImageSourceCreateWithURL(url as CFURL, nil) else {
            return NSImage(contentsOf: url)
        }

        let props = CGImageSourceCopyPropertiesAtIndex(src, 0, nil) as? [CFString: Any]
        let pixelWidth = props?[kCGImagePropertyPixelWidth] as? Int ?? 0
        let pixelHeight = props?[kCGImagePropertyPixelHeight] as? Int ?? 0

        var opts: [CFString: Any] = [
            kCGImageSourceCreateThumbnailFromImageAlways: true,
            kCGImageSourceCreateThumbnailWithTransform: true,
            kCGImageSourceShouldCacheImmediately: true,
        ]
        let maxDimension = max(pixelWidth, pixelHeight)
        if maxDimension > 0 {
            opts[kCGImageSourceThumbnailMaxPixelSize] = maxDimension
        }

        guard let cg = CGImageSourceCreateThumbnailAtIndex(src, 0, opts as CFDictionary) else {
            return NSImage(contentsOf: url)
        }
        return NSImage(cgImage: cg, size: .zero)
    }

    /// Internal rather than private so slice 12's tests can assert the key
    /// changes with mtime and size.
    static func cacheKey(for url: URL, size: Int) -> String {
        let attrs = try? FileManager.default.attributesOfItem(atPath: url.path)
        let mtime = (attrs?[.modificationDate] as? Date)?.timeIntervalSince1970 ?? 0
        return "\(url.path)|\(mtime)|\(size)"
    }
}
