import Foundation
import CoreGraphics
import ImageIO
import UniformTypeIdentifiers

/// The three export formats, mirroring the Windows `ImageFormat` enum. The folder
/// names must match Windows exactly so a folder organised on one platform reads
/// the same on the other.
enum ExportFormat: String, CaseIterable {
    case webp, png, jpg

    var folderName: String {
        switch self {
        case .webp: return "WebP"
        case .png:  return "PNG"
        case .jpg:  return "JPG"
        }
    }

    var fileExtension: String {
        switch self {
        case .webp: return "webp"
        case .png:  return "png"
        case .jpg:  return "jpg"
        }
    }
}

/// How the batch handles outputs that already exist — decided once for the whole
/// run, matching Windows.
enum OverwriteOption {
    case cancel, overwriteAll, skipExisting
}

struct ExportResult {
    let exported: Int
    let skipped: Int
    let failed: [String]
    let outputURL: URL
}

enum ExportError: Error {
    case cancelled          // the user chose Cancel at the overwrite prompt
    case decodeFailed
    case encodeFailed
}

/// Batch image export to WebP / PNG / JPG. Loads each source at full resolution
/// with EXIF orientation applied, encodes with the format's Windows-matching
/// settings and the source's metadata, and writes into a
/// `<directory>/<folderName>` subfolder.
actor ImageExporter {
    /// Per-file result, kept `Sendable` so it can flow back out of the task group.
    private enum FileOutcome: Sendable {
        case exported
        case skipped
        case failed(String)
        case cancelled
    }

    /// A source reduced to its `Sendable` essentials, so the per-file work can run
    /// off-actor without capturing a `PhotoItem` (which holds a non-`Sendable`
    /// `NSImage`).
    private struct Job: Sendable {
        let url: URL
        let fileName: String
    }

    private let maxConcurrent: Int

    init(maxConcurrent: Int = ProcessInfo.processInfo.activeProcessorCount) {
        self.maxConcurrent = max(1, maxConcurrent)
    }

    /// Export `photos` into `directory/<format.folderName>`.
    ///
    /// `overwrite` is called at most once, with the list of outputs that already
    /// exist, and its answer governs the whole batch; `.cancel` aborts before any
    /// file is written. `progress` is reported from completions (never from the
    /// dispatch loop) so the bar cannot jump backwards.
    func export(
        _ photos: [PhotoItem],
        in directory: URL,
        as format: ExportFormat,
        overwrite: @MainActor ([String]) async -> OverwriteOption,
        progress: @MainActor (Int, Int, String) -> Void
    ) async throws -> ExportResult {
        let outputFolder = directory.appendingPathComponent(format.folderName)
        try FileManager.default.createDirectory(at: outputFolder, withIntermediateDirectories: true)

        let jobs = photos.map { Job(url: $0.filePath, fileName: $0.fileName) }

        // Pre-flight scan for existing outputs; ask once and honour the answer for
        // the whole batch.
        let existing = jobs.compactMap { job -> String? in
            let name = Self.outputName(for: job.url, format: format)
            let url = outputFolder.appendingPathComponent(name)
            return FileManager.default.fileExists(atPath: url.path) ? name : nil
        }

        var overwriteOption: OverwriteOption = .overwriteAll
        if !existing.isEmpty {
            overwriteOption = await overwrite(existing)
            if overwriteOption == .cancel {
                throw ExportError.cancelled
            }
        }

        // Fixed for the rest of the run — bound to a `let` so the per-file tasks
        // capture an immutable value rather than the mutable prompt result.
        let policy = overwriteOption

        let total = jobs.count
        var exported = 0
        var skipped = 0
        var failed: [String] = []
        var processed = 0

        // Bounded concurrency: keep at most `maxConcurrent` files in flight, and
        // feed a replacement each time one completes. Progress is reported here,
        // on completion, so it only ever advances.
        await withTaskGroup(of: FileOutcome.self) { group in
            var next = 0

            @Sendable func makeTask(_ job: Job) -> @Sendable () -> FileOutcome {
                { Self.process(job, outputFolder: outputFolder, format: format, overwrite: policy) }
            }

            func dispatchNext() {
                guard next < jobs.count, !Task.isCancelled else { return }
                let job = jobs[next]
                next += 1
                group.addTask(operation: makeTask(job))
            }

            for _ in 0..<maxConcurrent { dispatchNext() }

            while let outcome = await group.next() {
                switch outcome {
                case .exported: exported += 1
                case .skipped: skipped += 1
                case .failed(let message): failed.append(message)
                case .cancelled: break
                }
                processed += 1
                let done = processed
                await progress(done, total, "Completed \(done) of \(total)")
                dispatchNext()
            }
        }

        return ExportResult(exported: exported, skipped: skipped, failed: failed, outputURL: outputFolder)
    }

    // MARK: - Per-file work (off-actor)

    private nonisolated static func outputName(for url: URL, format: ExportFormat) -> String {
        url.deletingPathExtension().lastPathComponent + "." + format.fileExtension
    }

    private nonisolated static func process(
        _ job: Job,
        outputFolder: URL,
        format: ExportFormat,
        overwrite: OverwriteOption
    ) -> FileOutcome {
        // A cancel between files stops new work; partial output stays on disk.
        if Task.isCancelled { return .cancelled }

        let outputURL = outputFolder.appendingPathComponent(outputName(for: job.url, format: format))

        if overwrite == .skipExisting, FileManager.default.fileExists(atPath: outputURL.path) {
            return .skipped
        }

        do {
            let data = try encode(job.url, as: format)
            try data.write(to: outputURL)
            return .exported
        } catch {
            return .failed("\(job.fileName): \(error.localizedDescription)")
        }
    }

    // MARK: - Encoding

    private nonisolated static func encode(_ url: URL, as format: ExportFormat) throws -> Data {
        guard let src = CGImageSourceCreateWithURL(url as CFURL, nil),
              let cgImage = loadOrientedCGImage(src)
        else { throw ExportError.decodeFailed }

        // The source's EXIF/GPS/XMP, marked upright to match the baked-in rotation
        // and sized to the decoded image.
        let metadata = ImageMetadata.uprightMetadata(from: src).map {
            ImageMetadata.withPixelSize($0, width: cgImage.width, height: cgImage.height)
        }

        switch format {
        case .webp:
            return try WebPEncoder.encode(
                cgImage,
                quality: 90,
                exif: metadata.flatMap {
                    ImageMetadata.exifBlob(from: $0, width: cgImage.width, height: cgImage.height)
                },
                xmp: metadata.flatMap(ImageMetadata.xmpData(from:))
            )
        case .png:
            // ImageIO's PNG encoder has no compression-level knob; Windows asks
            // for BestCompression, which is the closest we can express here.
            return try encodeImageIO(cgImage, type: .png, metadata: metadata, properties: [:])
        case .jpg:
            return try encodeImageIO(
                cgImage,
                type: .jpeg,
                metadata: metadata,
                properties: [kCGImageDestinationLossyCompressionQuality: 0.9]
            )
        }
    }

    /// Full-resolution decode with EXIF orientation baked in, so a sideways phone
    /// photo (orientation 6) exports upright — the same ImageIO transform the
    /// slice-02 thumbnail path uses.
    private nonisolated static func loadOrientedCGImage(_ src: CGImageSource) -> CGImage? {
        let props = CGImageSourceCopyPropertiesAtIndex(src, 0, nil) as? [CFString: Any]
        let pixelWidth = props?[kCGImagePropertyPixelWidth] as? Int ?? 0
        let pixelHeight = props?[kCGImagePropertyPixelHeight] as? Int ?? 0

        var opts: [CFString: Any] = [
            kCGImageSourceCreateThumbnailFromImageAlways: true,
            kCGImageSourceCreateThumbnailWithTransform: true,   // honours EXIF orientation
            kCGImageSourceShouldCacheImmediately: true,
        ]
        // Cap at the source's larger dimension so we get the full image, not a
        // downscale, while still applying the orientation transform.
        let maxDimension = max(pixelWidth, pixelHeight)
        if maxDimension > 0 {
            opts[kCGImageSourceThumbnailMaxPixelSize] = maxDimension
        }

        return CGImageSourceCreateThumbnailAtIndex(src, 0, opts as CFDictionary)
    }

    private nonisolated static func encodeImageIO(
        _ cgImage: CGImage,
        type: UTType,
        metadata: CGImageMetadata?,
        properties: [CFString: Any]
    ) throws -> Data {
        let data = NSMutableData()
        guard let dest = CGImageDestinationCreateWithData(
            data as CFMutableData, type.identifier as CFString, 1, nil
        ) else {
            throw ExportError.encodeFailed
        }
        if let metadata {
            CGImageDestinationAddImageAndMetadata(dest, cgImage, metadata, properties as CFDictionary)
        } else {
            CGImageDestinationAddImage(dest, cgImage, properties as CFDictionary)
        }
        guard CGImageDestinationFinalize(dest) else { throw ExportError.encodeFailed }
        return data as Data
    }
}
