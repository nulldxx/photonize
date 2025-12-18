using System.Diagnostics;
using System.IO;
using System.Collections.Concurrent;
using Photonize.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;

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
    /// Autopilot automatically analyzes each image and chooses optimal settings.
    /// Users can configure autopilot defaults in the Topaz Photo AI GUI application.
    /// </summary>
    /// <param name="photos">List of photos to upscale (can include folders)</param>
    /// <param name="directoryPath">Base directory containing the photos</param>
    /// <param name="overwriteCallback">Callback to ask user about overwriting existing files. Returns true to overwrite.</param>
    /// <param name="progress">Progress reporter for tracking operation progress</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <returns>Tuple with success status and message</returns>
    public async Task<(bool Success, string Message)> UpscalePhotosAsync(
        List<PhotoItem> photos,
        string directoryPath,
        Func<List<string>, bool>? overwriteCallback = null,
        IProgress<(int current, int total, string status)>? progress = null,
        CancellationToken cancellationToken = default)
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

            // Check for existing WebP files (final output format)
            var existingFiles = new List<string>();
            foreach (var photo in photos)
            {
                var outputFileNameWithoutExt = Path.GetFileNameWithoutExtension(photo.FileName);
                var webpFileName = outputFileNameWithoutExt + ".webp";
                var webpFilePath = Path.Combine(outputFolder, webpFileName);

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
                        return (false, "Upscale cancelled by user.");
                    }
                }
            }

            // Process photos in parallel with max 4 concurrent workers
            int startedCount = 0;
            int successCount = 0;
            int totalPhotos = photos.Count;
            var failedFiles = new ConcurrentBag<string>();
            var semaphore = new SemaphoreSlim(4, 4); // Max 4 concurrent operations

            var processingTasks = photos.Select(async (photo, index) =>
            {
                await semaphore.WaitAsync(cancellationToken);

                try
                {
                    // Check for cancellation
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    // Validate input file exists
                    if (!File.Exists(photo.FilePath))
                    {
                        failedFiles.Add($"{photo.FileName}: Input file not found");
                        return;
                    }

                    // Report progress (count how many have started)
                    var currentCount = Interlocked.Increment(ref startedCount);
                    progress?.Report((currentCount - 1, totalPhotos, $"Processing {photo.FileName}..."));

                    // Build command line arguments for tpai.exe using ArgumentList
                    var processStartInfo = new ProcessStartInfo
                    {
                        FileName = tpaiExePath,
                        WorkingDirectory = TopazInstallPath,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    // Add arguments individually
                    processStartInfo.ArgumentList.Add("--output");
                    processStartInfo.ArgumentList.Add(outputFolder);

                    // Preserve the original format
                    var extension = Path.GetExtension(photo.FilePath).TrimStart('.').ToLowerInvariant();
                    if (!string.IsNullOrEmpty(extension))
                    {
                        processStartInfo.ArgumentList.Add("--format");
                        processStartInfo.ArgumentList.Add(extension);
                    }

                    // Use autopilot mode
                    processStartInfo.ArgumentList.Add("--upscale");
                    processStartInfo.ArgumentList.Add(photo.FilePath);

                    // Determine expected output paths
                    var intermediateFileName = Path.GetFileName(photo.FilePath);
                    var intermediateFilePath = Path.Combine(outputFolder, intermediateFileName);
                    var webpFileNameWithoutExt = Path.GetFileNameWithoutExtension(photo.FilePath);
                    var webpFileName = webpFileNameWithoutExt + ".webp";
                    var webpFilePath = Path.Combine(outputFolder, webpFileName);

                    using (var process = Process.Start(processStartInfo))
                    {
                        if (process == null)
                        {
                            failedFiles.Add($"{photo.FileName}: Failed to start Topaz Photo AI process");
                            return;
                        }

                        // Read output asynchronously
                        var outputTask = process.StandardOutput.ReadToEndAsync();
                        var errorTask = process.StandardError.ReadToEndAsync();

                        // Wait for process with timeout (5 minutes per image)
                        if (!process.WaitForExit(300000))
                        {
                            process.Kill();
                            failedFiles.Add($"{photo.FileName}: Process timeout (exceeded 5 minutes)");
                            return;
                        }

                        var output = await outputTask;
                        var error = await errorTask;

                        // Check if output was created (ignore exit code - tpai.exe crashes often)
                        if (File.Exists(intermediateFilePath))
                        {
                            var fileInfo = new FileInfo(intermediateFilePath);
                            if (fileInfo.Length > 0)
                            {
                                // Convert to WebP
                                try
                                {
                                    using (var image = Image.Load(intermediateFilePath))
                                    {
                                        var webpEncoder = new WebpEncoder
                                        {
                                            Quality = 90,
                                            FileFormat = WebpFileFormatType.Lossy
                                        };
                                        await image.SaveAsync(webpFilePath, webpEncoder);
                                    }

                                    File.Delete(intermediateFilePath);

                                    if (File.Exists(webpFilePath) && new FileInfo(webpFilePath).Length > 0)
                                    {
                                        Interlocked.Increment(ref successCount);
                                    }
                                    else
                                    {
                                        failedFiles.Add($"{photo.FileName}: Failed to create WebP file");
                                    }
                                }
                                catch (Exception conversionEx)
                                {
                                    failedFiles.Add($"{photo.FileName}: WebP conversion failed - {conversionEx.Message}");
                                    if (File.Exists(intermediateFilePath))
                                    {
                                        try { File.Delete(intermediateFilePath); } catch { }
                                    }
                                }
                            }
                            else
                            {
                                failedFiles.Add($"{photo.FileName}: Output file is empty");
                            }
                        }
                        else
                        {
                            var errorDetails = $"Output file not created. Exit code: {process.ExitCode} (0x{process.ExitCode:X})";
                            if (!string.IsNullOrEmpty(error))
                                errorDetails += $"\nStderr: {error}";
                            failedFiles.Add($"{photo.FileName}: {errorDetails}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    failedFiles.Add($"{photo.FileName}: {ex.Message}");
                }
                finally
                {
                    semaphore.Release();
                }
            }).ToList();

            // Wait for all tasks to complete
            await Task.WhenAll(processingTasks);

            // Report final progress
            progress?.Report((successCount, totalPhotos, "Complete"));

            // Build result message
            string message;
            if (cancellationToken.IsCancellationRequested)
            {
                message = $"Operation cancelled. Processed {successCount} of {totalPhotos} photo(s).";
                return (successCount > 0, message);
            }
            else if (successCount == photos.Count)
            {
                message = $"Successfully upscaled {successCount} photo(s) with Topaz Photo AI in '{outputFolder}'";
            }
            else if (successCount > 0)
            {
                message = $"Upscaled {successCount} of {photos.Count} photo(s). {failedFiles.Count} failed.";
                if (failedFiles.Count > 0)
                {
                    message += $"\n\nFailed files:\n{string.Join("\n", failedFiles)}";
                }
            }
            else
            {
                message = $"Failed to upscale photos:\n{string.Join("\n", failedFiles)}";
            }

            return (successCount > 0, message);
        }
        catch (Exception ex)
        {
            return (false, $"Error during Topaz Photo AI upscale: {ex.Message}");
        }
    }
}
