using System.IO;
using Photonize.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;

namespace Photonize.Services;

public enum ImageFormat
{
    WebP,
    PNG,
    JPG
}

public enum OverwriteOption
{
    Cancel,
    OverwriteAll,
    SkipExisting
}

public class ImageExporter
{
    /// <summary>
    /// Exports a list of photos to the specified format in a subfolder.
    /// </summary>
    /// <param name="photos">List of photos to export</param>
    /// <param name="directoryPath">Base directory containing the photos</param>
    /// <param name="format">Target image format (WebP, PNG, or JPG)</param>
    /// <param name="overwriteCallback">Callback to ask user about overwriting existing files. Returns OverwriteOption.</param>
    /// <param name="progress">Progress reporter for tracking operation progress</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <returns>Tuple with success status and message</returns>
    public async Task<(bool Success, string Message)> ExportToFormatAsync(
        List<PhotoItem> photos,
        string directoryPath,
        ImageFormat format,
        Func<List<string>, OverwriteOption>? overwriteCallback = null,
        IProgress<(int current, int total, string status)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (photos == null || photos.Count == 0)
        {
            return (false, "No photos to export.");
        }

        if (string.IsNullOrEmpty(directoryPath) || !Directory.Exists(directoryPath))
        {
            return (false, "Invalid directory path.");
        }

        try
        {
            // Determine folder name and extension based on format
            string folderName = format.ToString();
            string extension = format switch
            {
                ImageFormat.WebP => ".webp",
                ImageFormat.PNG => ".png",
                ImageFormat.JPG => ".jpg",
                _ => throw new ArgumentException($"Unsupported format: {format}")
            };

            // Create subfolder
            var outputFolder = Path.Combine(directoryPath, folderName);
            if (!Directory.Exists(outputFolder))
            {
                Directory.CreateDirectory(outputFolder);
            }

            // Check for existing files
            var existingFiles = new List<string>();
            foreach (var photo in photos)
            {
                var originalFileName = Path.GetFileNameWithoutExtension(photo.FileName);
                var outputFileName = originalFileName + extension;
                var outputFilePath = Path.Combine(outputFolder, outputFileName);

                if (File.Exists(outputFilePath))
                {
                    existingFiles.Add(outputFileName);
                }
            }

            // Ask about overwriting if there are existing files
            OverwriteOption overwriteOption = OverwriteOption.OverwriteAll;
            if (existingFiles.Count > 0)
            {
                if (overwriteCallback != null)
                {
                    overwriteOption = overwriteCallback(existingFiles);
                    if (overwriteOption == OverwriteOption.Cancel)
                    {
                        return (false, "Export cancelled by user.");
                    }
                }
            }

            // Export photos using worker queue for parallel processing
            int exportedCount = 0;
            int skippedCount = 0;
            int totalPhotos = photos.Count;
            List<string> failedFiles = new List<string>();
            var lockObj = new object();

            var workerCount = Math.Max(1, Environment.ProcessorCount);
            using var workerQueue = new WorkerQueue<PhotoItem>(workerCount: workerCount, staggerDelay: TimeSpan.FromMilliseconds(100));

            // Subscribe to completion events
            workerQueue.WorkItemCompleted += (sender, args) =>
            {
                int currentCount;
                lock (lockObj)
                {
                    exportedCount++;
                    currentCount = exportedCount + skippedCount + failedFiles.Count;
                }
                progress?.Report((currentCount, totalPhotos, $"Completed {currentCount} of {totalPhotos}"));
            };

            workerQueue.WorkItemFailed += (sender, args) =>
            {
                int currentCount;
                lock (lockObj)
                {
                    failedFiles.Add($"{args.Item.FileName}: {args.Exception.Message}");
                    currentCount = exportedCount + skippedCount + failedFiles.Count;
                }
                progress?.Report((currentCount, totalPhotos, $"Completed {currentCount} of {totalPhotos}"));
            };

            // Enqueue all photos for processing
            foreach (var photo in photos)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                var originalFileName = Path.GetFileNameWithoutExtension(photo.FileName);
                var outputFileName = originalFileName + extension;
                var outputFilePath = Path.Combine(outputFolder, outputFileName);

                // Skip if file exists and user chose to skip existing files
                if (overwriteOption == OverwriteOption.SkipExisting && File.Exists(outputFilePath))
                {
                    int currentCount;
                    lock (lockObj)
                    {
                        skippedCount++;
                        currentCount = exportedCount + skippedCount + failedFiles.Count;
                    }
                    progress?.Report((currentCount, totalPhotos, $"Completed {currentCount} of {totalPhotos}"));
                    continue;
                }

                workerQueue.Enqueue(photo, async (item, ct) =>
                {
                    ct.ThrowIfCancellationRequested();

                    var fileName = Path.GetFileNameWithoutExtension(item.FileName);
                    var outputFileNameFinal = fileName + extension;
                    var outputFilePathFinal = Path.Combine(outputFolder, outputFileNameFinal);

                    // Load the image using ImageSharp
                    using (var image = await Image.LoadAsync(item.FilePath, ct))
                    {
                        // Save with format-specific encoder
                        switch (format)
                        {
                            case ImageFormat.WebP:
                                var webpEncoder = new WebpEncoder
                                {
                                    Quality = 90,
                                    FileFormat = WebpFileFormatType.Lossy
                                };
                                await image.SaveAsync(outputFilePathFinal, webpEncoder, ct);
                                break;

                            case ImageFormat.PNG:
                                var pngEncoder = new PngEncoder
                                {
                                    CompressionLevel = PngCompressionLevel.BestCompression
                                };
                                await image.SaveAsync(outputFilePathFinal, pngEncoder, ct);
                                break;

                            case ImageFormat.JPG:
                                var jpgEncoder = new JpegEncoder
                                {
                                    Quality = 90
                                };
                                await image.SaveAsync(outputFilePathFinal, jpgEncoder, ct);
                                break;
                        }
                    }
                }, cancellationToken);
            }

            // Wait for all work to complete
            await workerQueue.WaitForCompletionAsync(cancellationToken: cancellationToken);

            // Report final progress
            progress?.Report((exportedCount + skippedCount + failedFiles.Count, totalPhotos, "Complete"));

            // Build result message
            string message;
            if (exportedCount == photos.Count)
            {
                message = $"Successfully exported {exportedCount} photo(s) to {format} format in '{outputFolder}'";
            }
            else if (exportedCount > 0 || skippedCount > 0)
            {
                var parts = new List<string>();

                if (exportedCount > 0)
                {
                    parts.Add($"{exportedCount} exported");
                }
                if (skippedCount > 0)
                {
                    parts.Add($"{skippedCount} skipped");
                }
                if (failedFiles.Count > 0)
                {
                    parts.Add($"{failedFiles.Count} failed");
                }

                message = $"Export complete: {string.Join(", ", parts)} of {photos.Count} photo(s).";

                if (failedFiles.Count > 0)
                {
                    message += $"\n\nFailed files:\n{string.Join("\n", failedFiles)}";
                }
            }
            else
            {
                message = $"Failed to export photos:\n{string.Join("\n", failedFiles)}";
            }

            return (exportedCount > 0, message);
        }
        catch (Exception ex)
        {
            return (false, $"Error during {format} export: {ex.Message}");
        }
    }
}
