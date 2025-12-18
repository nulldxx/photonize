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
            List<string> failedFiles = new List<string>();

            await Task.Run(() =>
            {
                foreach (var photo in photos)
                {
                    try
                    {
                        // Validate input file exists
                        if (!File.Exists(photo.FilePath))
                        {
                            failedFiles.Add($"{photo.FileName}: Input file not found");
                            continue;
                        }

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
                        processStartInfo.ArgumentList.Add("--output");
                        processStartInfo.ArgumentList.Add(outputFolder);
                        processStartInfo.ArgumentList.Add("--compression");
                        processStartInfo.ArgumentList.Add("2");

                        // Preserve the original format
                        var extension = Path.GetExtension(photo.FilePath).TrimStart('.').ToLowerInvariant();
                        if (!string.IsNullOrEmpty(extension))
                        {
                            processStartInfo.ArgumentList.Add("--format");
                            processStartInfo.ArgumentList.Add(extension);
                        }

                        // Upscale settings: 2x with High Fidelity V2 model
                        processStartInfo.ArgumentList.Add("--upscale");
                        processStartInfo.ArgumentList.Add("scale=2.0");
                        processStartInfo.ArgumentList.Add("model=High Fidelity V2");

                        // Optional: denoise, deblur, detail parameters (defaults are usually fine)
                        // processStartInfo.ArgumentList.Add("param1=0.0");  // Minor Denoise
                        // processStartInfo.ArgumentList.Add("param2=0.0");  // Minor Deblur
                        // processStartInfo.ArgumentList.Add("param3=0.0");  // Fix Compression

                        // Face Recovery settings - low quality
                        processStartInfo.ArgumentList.Add("--recover-faces");
                        processStartInfo.ArgumentList.Add("quality=low");

                        // Note: --showSettings can be omitted if it causes issues
                        // processStartInfo.ArgumentList.Add("--showSettings");

                        processStartInfo.ArgumentList.Add(photo.FilePath);

                        // Determine expected intermediate output file path (Topaz output in original format)
                        var intermediateFileName = Path.GetFileName(photo.FilePath);
                        var intermediateFilePath = Path.Combine(outputFolder, intermediateFileName);

                        // Determine final WebP output file path
                        var webpFileNameWithoutExt = Path.GetFileNameWithoutExtension(photo.FilePath);
                        var webpFileName = webpFileNameWithoutExt + ".webp";
                        var webpFilePath = Path.Combine(outputFolder, webpFileName);

                        using (var process = Process.Start(processStartInfo))
                        {
                            if (process == null)
                            {
                                failedFiles.Add($"{photo.FileName}: Failed to start Topaz Photo AI process");
                                continue;
                            }

                            // Read output asynchronously to avoid blocking
                            var outputTask = process.StandardOutput.ReadToEndAsync();
                            var errorTask = process.StandardError.ReadToEndAsync();

                            // Wait for process with timeout (5 minutes per image should be plenty)
                            if (!process.WaitForExit(300000))
                            {
                                process.Kill();
                                failedFiles.Add($"{photo.FileName}: Process timeout (exceeded 5 minutes)");
                                continue;
                            }

                            var output = outputTask.Result;
                            var error = errorTask.Result;

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
                                        }
                                        else
                                        {
                                            failedFiles.Add($"{photo.FileName}: Failed to create WebP file");
                                        }
                                    }
                                    catch (Exception conversionEx)
                                    {
                                        failedFiles.Add($"{photo.FileName}: WebP conversion failed - {conversionEx.Message}");
                                        // Try to clean up intermediate file if it still exists
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
                                // Output file wasn't created - this is a real failure
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
