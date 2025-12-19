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
    /// <returns>Tuple with success status and message</returns>
    public async Task<(bool Success, string Message)> ExportToFormatAsync(
        List<PhotoItem> photos,
        string directoryPath,
        ImageFormat format,
        Func<List<string>, OverwriteOption>? overwriteCallback = null)
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

            // Export photos
            int exportedCount = 0;
            int skippedCount = 0;
            List<string> failedFiles = new List<string>();

            await Task.Run(() =>
            {
                foreach (var photo in photos)
                {
                    try
                    {
                        var originalFileName = Path.GetFileNameWithoutExtension(photo.FileName);
                        var outputFileName = originalFileName + extension;
                        var outputFilePath = Path.Combine(outputFolder, outputFileName);

                        // Skip if file exists and user chose to skip existing files
                        if (overwriteOption == OverwriteOption.SkipExisting && File.Exists(outputFilePath))
                        {
                            skippedCount++;
                            continue;
                        }

                        // Load the image using ImageSharp
                        using (var image = Image.Load(photo.FilePath))
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
                                    image.Save(outputFilePath, webpEncoder);
                                    break;

                                case ImageFormat.PNG:
                                    var pngEncoder = new PngEncoder
                                    {
                                        CompressionLevel = PngCompressionLevel.BestCompression
                                    };
                                    image.Save(outputFilePath, pngEncoder);
                                    break;

                                case ImageFormat.JPG:
                                    var jpgEncoder = new JpegEncoder
                                    {
                                        Quality = 90
                                    };
                                    image.Save(outputFilePath, jpgEncoder);
                                    break;
                            }
                        }

                        exportedCount++;
                    }
                    catch (Exception ex)
                    {
                        failedFiles.Add($"{photo.FileName}: {ex.Message}");
                    }
                }
            });

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
