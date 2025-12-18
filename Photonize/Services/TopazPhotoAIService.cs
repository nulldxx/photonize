using System.Diagnostics;
using System.IO;
using Photonize.Models;

namespace Photonize.Services;

public class TopazPhotoAIService
{
    private const string TopazInstallPath = @"C:\Program Files\Topaz Labs LLC\Topaz Photo AI";
    private const string TopazExeName = "tpai.exe";

    /// <summary>
    /// Checks if Topaz Photo AI is installed by looking for tpai.exe
    /// </summary>
    public bool IsTopazInstalled()
    {
        var tpaiPath = Path.Combine(TopazInstallPath, TopazExeName);
        return File.Exists(tpaiPath);
    }

    /// <summary>
    /// Gets the full path to tpai.exe
    /// </summary>
    private string GetTopazExePath()
    {
        return Path.Combine(TopazInstallPath, TopazExeName);
    }

    /// <summary>
    /// Upscales a list of photos using Topaz Photo AI with autopilot settings.
    /// </summary>
    /// <param name="photos">List of photos to upscale (can include folders)</param>
    /// <param name="directoryPath">Base directory containing the photos</param>
    /// <param name="overwriteCallback">Callback to ask user about overwriting existing files. Returns true to overwrite.</param>
    /// <returns>Tuple with success status and message</returns>
    public async Task<(bool Success, string Message)> UpscalePhotosAsync(
        List<PhotoItem> photos,
        string directoryPath,
        Func<List<string>, bool>? overwriteCallback = null)
    {
        if (photos == null || photos.Count == 0)
        {
            return (false, "No photos to upscale.");
        }

        if (string.IsNullOrEmpty(directoryPath) || !Directory.Exists(directoryPath))
        {
            return (false, "Invalid directory path.");
        }

        if (!IsTopazInstalled())
        {
            return (false, $"Topaz Photo AI not found at: {TopazInstallPath}");
        }

        // Expand folders into their image files
        var expandedPhotos = new List<PhotoItem>();
        foreach (var item in photos)
        {
            if (item.IsFolder && Directory.Exists(item.FilePath))
            {
                // Get all image files in the folder
                var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif", ".tiff" };
                var imageFiles = Directory.GetFiles(item.FilePath)
                    .Where(f => imageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .ToList();

                foreach (var imageFile in imageFiles)
                {
                    expandedPhotos.Add(new PhotoItem
                    {
                        FilePath = imageFile,
                        FileName = Path.GetFileName(imageFile),
                        IsFolder = false
                    });
                }
            }
            else if (!item.IsFolder)
            {
                expandedPhotos.Add(item);
            }
        }

        if (expandedPhotos.Count == 0)
        {
            return (false, "No image files found to upscale.");
        }

        photos = expandedPhotos;

        try
        {
            var tpaiExePath = GetTopazExePath();
            var outputFolder = Path.Combine(directoryPath, "TopazUpscaled");

            // Create output folder if it doesn't exist
            if (!Directory.Exists(outputFolder))
            {
                Directory.CreateDirectory(outputFolder);
            }

            // Check for existing files
            var existingFiles = new List<string>();
            foreach (var photo in photos)
            {
                var outputFileName = Path.GetFileName(photo.FileName);
                var outputFilePath = Path.Combine(outputFolder, outputFileName);

                if (File.Exists(outputFilePath))
                {
                    existingFiles.Add(outputFileName);
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
                        return (false, "Upscale cancelled by user.");
                    }
                }
            }

            // Process each photo
            int processedCount = 0;
            List<string> failedFiles = new List<string>();

            await Task.Run(() =>
            {
                foreach (var photo in photos)
                {
                    try
                    {
                        // Build command line arguments for tpai.exe
                        // Using autopilot (no custom settings) - just specify output and format
                        var arguments = $"--output \"{outputFolder}\" --compression 2 --showSettings \"{photo.FilePath}\"";

                        var processStartInfo = new ProcessStartInfo
                        {
                            FileName = tpaiExePath,
                            Arguments = arguments,
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true
                        };

                        using (var process = Process.Start(processStartInfo))
                        {
                            if (process == null)
                            {
                                failedFiles.Add($"{photo.FileName}: Failed to start Topaz Photo AI process");
                                continue;
                            }

                            // Read output
                            var output = process.StandardOutput.ReadToEnd();
                            var error = process.StandardError.ReadToEnd();

                            process.WaitForExit();

                            if (process.ExitCode != 0)
                            {
                                failedFiles.Add($"{photo.FileName}: Topaz Photo AI exited with code {process.ExitCode}. Error: {error}");
                            }
                            else
                            {
                                processedCount++;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        failedFiles.Add($"{photo.FileName}: {ex.Message}");
                    }
                }
            });

            // Build result message
            string message;
            if (processedCount == photos.Count)
            {
                message = $"Successfully upscaled {processedCount} photo(s) with Topaz Photo AI in '{outputFolder}'";
            }
            else if (processedCount > 0)
            {
                message = $"Upscaled {processedCount} of {photos.Count} photo(s). {failedFiles.Count} failed.";
                if (failedFiles.Count > 0)
                {
                    message += $"\n\nFailed files:\n{string.Join("\n", failedFiles)}";
                }
            }
            else
            {
                message = $"Failed to upscale photos:\n{string.Join("\n", failedFiles)}";
            }

            return (processedCount > 0, message);
        }
        catch (Exception ex)
        {
            return (false, $"Error during Topaz Photo AI upscale: {ex.Message}");
        }
    }
}
