using System.Diagnostics;
using System.IO;
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
            var tempFolder = Path.Combine(Path.GetTempPath(), $"Photonize_Topaz_{Guid.NewGuid():N}");

            // Create output folder if it doesn't exist
            if (!Directory.Exists(outputFolder))
            {
                Directory.CreateDirectory(outputFolder);
            }

            // Create temp folder for intermediate files
            Directory.CreateDirectory(tempFolder);

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

            // Process each photo
            int processedCount = 0;
            int totalPhotos = photos.Count;
            List<string> failedFiles = new List<string>();

            try
            {
                await Task.Run(() =>
                {
                for (int i = 0; i < photos.Count; i++)
                {
                    // Check for cancellation
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    var photo = photos[i];

                    try
                    {
                        // Validate input file exists
                        if (!File.Exists(photo.FilePath))
                        {
                            failedFiles.Add($"{photo.FileName}: Input file not found");
                            continue;
                        }

                        // Retry up to 3 times for exit code -2 (transient errors)
                        const int maxRetries = 3;
                        bool success = false;
                        int lastExitCode = 0;
                        string lastError = "";

                        for (int attempt = 1; attempt <= maxRetries && !success; attempt++)
                        {
                            // Report progress
                            var statusMessage = attempt == 1 
                                ? $"Processing {photo.FileName}..." 
                                : $"Processing {photo.FileName}... (retry {attempt}/{maxRetries})";
                            progress?.Report((i, totalPhotos, statusMessage));

                        // Build command line arguments for tpai.exe using ArgumentList
                        // This handles quoting automatically and avoids string formatting issues
                            var processStartInfo = new ProcessStartInfo
                            {
                                FileName = tpaiExePath,
                                WorkingDirectory = TopazInstallPath,  // Set working directory to Topaz folder for DLL dependencies
                                UseShellExecute = false,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                CreateNoWindow = true
                            };

                            // Add arguments individually - this is safer than building a string
                            // Write to temp folder instead of network folder
                            processStartInfo.ArgumentList.Add("--output");
                            processStartInfo.ArgumentList.Add(tempFolder);

                            // Preserve the original format
                            var extension = Path.GetExtension(photo.FilePath).TrimStart('.').ToLowerInvariant();
                            if (!string.IsNullOrEmpty(extension))
                            {
                                processStartInfo.ArgumentList.Add("--format");
                                processStartInfo.ArgumentList.Add(extension);
                            }

                            // Use autopilot mode - users can configure defaults in Topaz Photo AI GUI
                            processStartInfo.ArgumentList.Add("--upscale");

                            processStartInfo.ArgumentList.Add(photo.FilePath);

                            // Determine expected intermediate output file path (Topaz output in temp folder)
                            var intermediateFileName = Path.GetFileName(photo.FilePath);
                            var intermediateFilePath = Path.Combine(tempFolder, intermediateFileName);

                            // Determine final WebP output file path
                            var webpFileNameWithoutExt = Path.GetFileNameWithoutExtension(photo.FilePath);
                            var webpFileName = webpFileNameWithoutExt + ".webp";
                            var webpFilePath = Path.Combine(outputFolder, webpFileName);

                            using (var process = Process.Start(processStartInfo))
                            {
                                if (process == null)
                                {
                                    lastError = "Failed to start Topaz Photo AI process";
                                    break; // Don't retry if we can't start the process
                                }

                                // Read output asynchronously to avoid blocking
                                var outputTask = process.StandardOutput.ReadToEndAsync();
                                var errorTask = process.StandardError.ReadToEndAsync();

                                // Wait for process with timeout (5 minutes per image should be plenty)
                                if (!process.WaitForExit(300000))
                                {
                                    process.Kill();
                                    lastError = "Process timeout (exceeded 5 minutes)";
                                    break; // Don't retry timeouts
                                }

                                var output = outputTask.Result;
                                var error = errorTask.Result;
                                lastExitCode = process.ExitCode;
                                lastError = error;

                                // Check if intermediate output file was created successfully (ignore exit code)
                                // tpai.exe often crashes during exit even after successful processing
                                if (File.Exists(intermediateFilePath))
                                {
                                    // Verify the intermediate file has content
                                    var fileInfo = new FileInfo(intermediateFilePath);
                                    if (fileInfo.Length > 0)
                                    {
                                        // Convert to WebP and delete intermediate file
                                        try
                                        {
                                            using (var image = Image.Load(intermediateFilePath))
                                            {
                                                var webpEncoder = new WebpEncoder
                                                {
                                                    Quality = 90,
                                                    FileFormat = WebpFileFormatType.Lossy
                                                };
                                                image.Save(webpFilePath, webpEncoder);
                                            }

                                            // Delete the intermediate file
                                            File.Delete(intermediateFilePath);

                                            // Verify WebP file was created
                                            if (File.Exists(webpFilePath) && new FileInfo(webpFilePath).Length > 0)
                                            {
                                                processedCount++;
                                                success = true; // Success! Exit retry loop
                                            }
                                            else
                                            {
                                                lastError = "Failed to create WebP file";
                                            }
                                        }
                                        catch (Exception conversionEx)
                                        {
                                            lastError = $"WebP conversion failed - {conversionEx.Message}";
                                            // Try to clean up intermediate file if it still exists
                                            if (File.Exists(intermediateFilePath))
                                            {
                                                try { File.Delete(intermediateFilePath); } catch { }
                                            }
                                            break; // Don't retry conversion errors
                                        }
                                    }
                                    else
                                    {
                                        lastError = "Output file is empty";
                                        break; // Don't retry empty files
                                    }
                                }
                                else
                                {
                                    // Output file wasn't created
                                    // Check if this is exit code -2 (transient error) and we have retries left
                                    if (lastExitCode == -2 && attempt < maxRetries)
                                    {
                                        // Will retry on next iteration
                                        continue;
                                    }
                                    else
                                    {
                                        // Out of retries or different error - record failure
                                        lastError = $"Output file not created. Exit code: {lastExitCode} (0x{lastExitCode:X})";
                                        if (!string.IsNullOrEmpty(error))
                                            lastError += $"\nStderr: {error}";
                                        break;
                                    }
                                }
                            }
                        }

                        // If we didn't succeed after all retries, add to failed files
                        if (!success)
                        {
                            failedFiles.Add($"{photo.FileName}: {lastError}");
                        }
                    }
                    catch (Exception ex)
                    {
                        failedFiles.Add($"{photo.FileName}: {ex.Message}");
                    }
                }
                }, cancellationToken);
            }
            finally
            {
                // Clean up temp folder
                try
                {
                    if (Directory.Exists(tempFolder))
                    {
                        Directory.Delete(tempFolder, recursive: true);
                    }
                }
                catch
                {
                    // Ignore cleanup errors - temp folder will be cleaned up by OS eventually
                }
            }

            // Report final progress
            progress?.Report((processedCount, totalPhotos, "Complete"));

            // Build result message
            string message;
            if (cancellationToken.IsCancellationRequested)
            {
                message = $"Operation cancelled. Processed {processedCount} of {totalPhotos} photo(s).";
                return (processedCount > 0, message);
            }
            else if (processedCount == photos.Count)
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
