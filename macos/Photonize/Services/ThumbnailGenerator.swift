import Foundation
import AppKit
import UniformTypeIdentifiers

class ThumbnailGenerator {
    private static let supportedExtensions = ["jpg", "jpeg", "png", "bmp", "gif", "tiff", "tif", "webp", "heic", "heif"]

    func isImageFile(_ url: URL) -> Bool {
        let ext = url.pathExtension.lowercased()
        return Self.supportedExtensions.contains(ext)
    }

    func generateThumbnail(for url: URL, maxSize: Int) async -> NSImage? {
        return await withCheckedContinuation { continuation in
            DispatchQueue.global(qos: .userInitiated).async {
                guard let image = NSImage(contentsOf: url) else {
                    continuation.resume(returning: nil)
                    return
                }

                let thumbnail = self.resizeImage(image, maxSize: maxSize)
                continuation.resume(returning: thumbnail)
            }
        }
    }

    func generatePreview(for url: URL) async -> NSImage? {
        return await withCheckedContinuation { continuation in
            DispatchQueue.global(qos: .userInitiated).async {
                let image = NSImage(contentsOf: url)
                continuation.resume(returning: image)
            }
        }
    }

    func generateFolderThumbnail(size: Int) -> NSImage {
        let image = NSImage(size: NSSize(width: size, height: size))

        image.lockFocus()

        // Draw folder icon
        let folderColor = NSColor(red: 1.0, green: 0.76, blue: 0.03, alpha: 1.0) // Yellow/gold
        let borderColor = NSColor(red: 0.78, green: 0.59, blue: 0.0, alpha: 1.0)

        // Folder tab (top part)
        let tabRect = NSRect(
            x: CGFloat(size) * 0.1,
            y: CGFloat(size) * 0.5,
            width: CGFloat(size) * 0.4,
            height: CGFloat(size) * 0.15
        )
        let tabPath = NSBezierPath(roundedRect: tabRect, xRadius: 3, yRadius: 3)
        folderColor.setFill()
        borderColor.setStroke()
        tabPath.lineWidth = 2
        tabPath.fill()
        tabPath.stroke()

        // Folder body (main part)
        let bodyRect = NSRect(
            x: CGFloat(size) * 0.1,
            y: CGFloat(size) * 0.2,
            width: CGFloat(size) * 0.8,
            height: CGFloat(size) * 0.5
        )
        let bodyPath = NSBezierPath(roundedRect: bodyRect, xRadius: 5, yRadius: 5)
        folderColor.setFill()
        borderColor.setStroke()
        bodyPath.lineWidth = 2
        bodyPath.fill()
        bodyPath.stroke()

        image.unlockFocus()

        return image
    }

    private func resizeImage(_ image: NSImage, maxSize: Int) -> NSImage {
        let size = image.size
        let aspectRatio = size.width / size.height

        var newWidth: CGFloat
        var newHeight: CGFloat

        if size.width > size.height {
            newWidth = CGFloat(maxSize)
            newHeight = newWidth / aspectRatio
        } else {
            newHeight = CGFloat(maxSize)
            newWidth = newHeight * aspectRatio
        }

        let newSize = NSSize(width: newWidth, height: newHeight)
        let newImage = NSImage(size: newSize)

        newImage.lockFocus()
        image.draw(
            in: NSRect(origin: .zero, size: newSize),
            from: NSRect(origin: .zero, size: size),
            operation: .sourceOver,
            fraction: 1.0
        )
        newImage.unlockFocus()

        return newImage
    }
}
