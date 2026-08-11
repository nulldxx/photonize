import Foundation
import AppKit
import AVFoundation
import ImageIO
import UniformTypeIdentifiers

class ThumbnailGenerator {
    private static let supportedExtensions = ["jpg", "jpeg", "png", "bmp", "gif", "tiff", "tif", "webp", "heic", "heif"]
    /// The Windows three. AVFoundation decodes these natively on every supported
    /// Mac, so — unlike Windows — no FFmpeg is required. Deliberately narrow: a
    /// `.mkv` AVFoundation can't decode would just show a broken tile.
    private static let videoExtensions = ["mp4", "mov", "m4v"]

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

    func isVideoFile(_ url: URL) -> Bool {
        let ext = url.pathExtension.lowercased()
        return Self.videoExtensions.contains(ext)
    }

    /// Images and videos together — what the grid enumerates so a mixed folder
    /// shows and renumbers both.
    func isMediaFile(_ url: URL) -> Bool {
        isImageFile(url) || isVideoFile(url)
    }

    func generateThumbnail(for url: URL, maxSize: Int) async -> NSImage? {
        let isVideo = isVideoFile(url)
        // Keep the key a Swift `String` (Sendable) across the closure boundary and
        // bridge to `NSString` only at the cache calls.
        let key = Self.cacheKey(for: url, size: maxSize, isVideo: isVideo)
        if let cached = Self.cache.object(forKey: key as NSString) {
            return cached
        }

        if isVideo {
            // AVFoundation decodes MP4/MOV/M4V natively; a failure yields a drawn
            // placeholder (cached, like a real frame) so the tile isn't blank.
            let frame = await Self.decodeVideoThumbnail(url: url, maxSize: maxSize)
            let thumbnail = frame ?? Self.brokenVideoPlaceholder(maxSize: maxSize)
            Self.cache.setObject(thumbnail, forKey: key as NSString)
            return thumbnail
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

    // MARK: - Video

    /// The frame time to grab for a video thumbnail: 1 s, or 0.1 s for clips
    /// shorter than 2 s where 1 s would be at or beyond the end. Matches Windows,
    /// and internal so slice 12's tests can assert the choice without decoding.
    static func videoThumbnailSeconds(forDuration duration: Double) -> Double {
        duration < 2 ? 0.1 : 1
    }

    /// One frame of the video via AVFoundation. `appliesPreferredTrackTransform`
    /// honours rotation metadata so portrait iPhone clips come out upright rather
    /// than sideways — the equivalent of the EXIF transform on the image path.
    private static func decodeVideoThumbnail(url: URL, maxSize: Int) async -> NSImage? {
        let asset = AVURLAsset(url: url)

        let durationSeconds: Double
        do {
            let duration = try await asset.load(.duration)
            durationSeconds = duration.seconds.isFinite ? duration.seconds : 0
        } catch {
            return nil
        }

        let generator = AVAssetImageGenerator(asset: asset)
        generator.appliesPreferredTrackTransform = true
        generator.maximumSize = CGSize(width: maxSize, height: maxSize)

        let seconds = videoThumbnailSeconds(forDuration: durationSeconds)
        let time = CMTime(seconds: seconds, preferredTimescale: 600)

        if #available(macOS 14.0, *) {
            do {
                let cg = try await generator.image(at: time).image
                return NSImage(cgImage: cg, size: .zero)
            } catch {
                return nil
            }
        } else {
            // macOS 13 fallback: `copyCGImage` is synchronous and deprecated but
            // functional. This runs off the main actor already (the loader's task
            // group), so a brief blocking decode here is fine.
            let cg = try? generator.copyCGImage(at: time, actualTime: nil)
            return cg.map { NSImage(cgImage: $0, size: .zero) }
        }
    }

    /// A drawn "broken video" tile for a clip AVFoundation cannot decode (e.g. a
    /// text file renamed to `.mp4`), mirroring the Windows placeholder.
    private static func brokenVideoPlaceholder(maxSize: Int) -> NSImage {
        let side = CGFloat(max(64, maxSize))
        let image = NSImage(size: NSSize(width: side, height: side))
        image.lockFocus()
        NSColor(calibratedWhite: 0.15, alpha: 1).setFill()
        NSRect(x: 0, y: 0, width: side, height: side).fill()

        let config = NSImage.SymbolConfiguration(pointSize: side * 0.4, weight: .regular)
        if let symbol = NSImage(systemSymbolName: "video.slash", accessibilityDescription: "Unplayable video")?
            .withSymbolConfiguration(config) {
            let symbolSize = symbol.size
            let origin = NSPoint(x: (side - symbolSize.width) / 2, y: (side - symbolSize.height) / 2)
            NSColor(calibratedWhite: 0.6, alpha: 1).set()
            symbol.draw(at: origin, from: .zero, operation: .sourceOver, fraction: 1)
        }
        image.unlockFocus()
        return image
    }

    // MARK: - Cache key

    /// Internal rather than private so slice 12's tests can assert the key
    /// changes with mtime and size. The `isVideo` component keeps a video frame
    /// and an image of the same path/size in distinct slots.
    static func cacheKey(for url: URL, size: Int, isVideo: Bool = false) -> String {
        let attrs = try? FileManager.default.attributesOfItem(atPath: url.path)
        let mtime = (attrs?[.modificationDate] as? Date)?.timeIntervalSince1970 ?? 0
        let kind = isVideo ? "v" : "i"
        return "\(url.path)|\(mtime)|\(size)|\(kind)"
    }
}
