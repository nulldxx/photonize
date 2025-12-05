using System.IO;
using Photonize.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;

namespace Photonize.Services;

public class WebPExporter
{
    /// <summary>
    /// Exports a list of photos to WebP format in a "WebP" subfolder.
    /// </summary>
    /// <param name="photos">List of photos to export</param>
    /// <param name="directoryPath">Base directory containing the photos</param>
    /// <param name="overwriteCallback">Callback to ask user about overwriting existing files. Returns true to overwrite.</param>
    /// <returns>Tuple with success status and message</returns>
    public async Task<(bool Success, string Message)> ExportToWebPAsync(
        List<PhotoItem> photos,
        string directoryPath,
        Func<List<string>, bool>? overwriteCallback = null)
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
            // Create WebP subfolder
            var webpFolder = Path.Combine(directoryPath, "WebP");
            if (!Directory.Exists(webpFolder))
            {
                Directory.CreateDirectory(webpFolder);
            }

            // Check for existing files
            var existingFiles = new List<string>();
            foreach (var photo in photos)
            {
                var originalFileName = Path.GetFileNameWithoutExtension(photo.FileName);
                var webpFileName = originalFileName + ".webp";
                var webpFilePath = Path.Combine(webpFolder, webpFileName);

                if (File.Exists(webpFilePath))
                {
                    existingFiles.Add(webpFileName);
                }
            }

            // Ask about overwriting if there are existing files
            if (existingFiles.Count > 0)
            {
                if (overwriteCallback != null)
                {
                    bool shouldOverwrite = overwriteCallback(existingFiles);
                    if (!shouldOverwrite)
                    {
                        return (false, "Export cancelled by user.");
                    }
                }
            }

            // Export photos
            int exportedCount = 0;
            List<string> failedFiles = new List<string>();

            await Task.Run(() =>
            {
                foreach (var photo in photos)
                {
                    try
                    {
                        var originalFileName = Path.GetFileNameWithoutExtension(photo.FileName);
                        var webpFileName = originalFileName + ".webp";
                        var webpFilePath = Path.Combine(webpFolder, webpFileName);

                        // Load the image using ImageSharp
                        using (var image = Image.Load(photo.FilePath))
                        {
                            // Configure WebP encoder for lossy compression at 90% quality
                            var encoder = new WebpEncoder
                            {
                                Quality = 90,
                                FileFormat = WebpFileFormatType.Lossy
                            };

                            // Save as WebP
                            image.Save(webpFilePath, encoder);
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
                message = $"Successfully exported {exportedCount} photo(s) to WebP format in '{webpFolder}'";
            }
            else if (exportedCount > 0)
            {
                message = $"Exported {exportedCount} of {photos.Count} photo(s). {failedFiles.Count} failed.";
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
            return (false, $"Error during WebP export: {ex.Message}");
        }
    }
}
