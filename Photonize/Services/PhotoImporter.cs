using System.IO;
using System.Text.RegularExpressions;
using Photonize.Models;
using Photonize.Views;

namespace Photonize.Services;

public class PhotoImporter
{
    private readonly ThumbnailGenerator _thumbnailGenerator;

    public PhotoImporter()
    {
        _thumbnailGenerator = new ThumbnailGenerator();
    }

    /// <summary>
    /// Import files into the target directory with the specified mode.
    /// </summary>
    /// <param name="sourceFiles">Files to import</param>
    /// <param name="existingPhotos">Current photos in the collection</param>
    /// <param name="targetDirectory">Directory to import into</param>
    /// <param name="prefix">Naming prefix to use</param>
    /// <param name="mode">Import mode (Append or Distribute)</param>
    /// <returns>Tuple of success status and message</returns>
    public async Task<(bool Success, string Message, List<PhotoItem> UpdatedPhotos)> ImportPhotosAsync(
        List<string> sourceFiles,
        List<PhotoItem> existingPhotos,
        string targetDirectory,
        string prefix,
        ImportMode mode)
    {
        return await Task.Run(async () =>
        {
            try
            {
                if (sourceFiles.Count == 0)
                    return (false, "No files to import.", new List<PhotoItem>());

                if (!Directory.Exists(targetDirectory))
                    return (false, "Target directory does not exist.", new List<PhotoItem>());

                // Filter out files that are already in the target directory
                var filesToImport = sourceFiles
                    .Where(f => Path.GetDirectoryName(Path.GetFullPath(f)) != Path.GetFullPath(targetDirectory))
                    .ToList();

                if (filesToImport.Count == 0)
                    return (false, "All selected files are already in the target directory.", new List<PhotoItem>());

                // Create interleaved or appended list
                List<(string SourcePath, bool IsNew, int FinalIndex)> fileOperations;

                if (mode == ImportMode.Distribute)
                {
                    fileOperations = CalculateDistributedPositions(existingPhotos, filesToImport);
                }
                else // Append
                {
                    fileOperations = CalculateAppendPositions(existingPhotos, filesToImport);
                }

                // Phase 1: Rename existing files to temporary names
                var tempMapping = new Dictionary<string, string>();
                foreach (var photo in existingPhotos)
                {
                    var tempPath = photo.FilePath + ".tmp_import";
                    File.Move(photo.FilePath, tempPath);
                    tempMapping[photo.FilePath] = tempPath;
                }

                // Phase 2: Copy new files and rename all to final names
                var updatedPhotos = new List<PhotoItem>();

                foreach (var (sourcePath, isNew, finalIndex) in fileOperations.OrderBy(o => o.FinalIndex))
                {
                    var extension = Path.GetExtension(sourcePath);
                    var finalName = $"{prefix}-{(finalIndex + 1):D5}{extension}";
                    var finalPath = Path.Combine(targetDirectory, finalName);

                    if (isNew)
                    {
                        // Copy new file to final location
                        File.Copy(sourcePath, finalPath, overwrite: false);
                    }
                    else
                    {
                        // Rename from temp location
                        var tempPath = tempMapping[sourcePath];
                        File.Move(tempPath, finalPath);
                    }

                    // Create PhotoItem for the final list
                    var photoItem = new PhotoItem
                    {
                        FilePath = finalPath,
                        FileName = finalName,
                        DisplayOrder = finalIndex
                    };

                    // Load thumbnail asynchronously
                    var thumbnail = await _thumbnailGenerator.GenerateThumbnailAsync(finalPath, 200);
                    photoItem.Thumbnail = thumbnail;

                    updatedPhotos.Add(photoItem);
                }

                var modeText = mode == ImportMode.Distribute ? "distributed" : "appended";
                return (true, $"Successfully {modeText} {filesToImport.Count} file(s).", updatedPhotos);
            }
            catch (Exception ex)
            {
                return (false, $"Error during import: {ex.Message}", new List<PhotoItem>());
            }
        });
    }

    /// <summary>
    /// Calculate file positions for append mode (new files at the end).
    /// </summary>
    private List<(string SourcePath, bool IsNew, int FinalIndex)> CalculateAppendPositions(
        List<PhotoItem> existingPhotos,
        List<string> newFiles)
    {
        var operations = new List<(string, bool, int)>();

        // Add existing photos first
        var sortedExisting = existingPhotos.OrderBy(p => p.DisplayOrder).ToList();
        for (int i = 0; i < sortedExisting.Count; i++)
        {
            operations.Add((sortedExisting[i].FilePath, false, i));
        }

        // Add new photos at the end
        for (int i = 0; i < newFiles.Count; i++)
        {
            operations.Add((newFiles[i], true, sortedExisting.Count + i));
        }

        return operations;
    }

    /// <summary>
    /// Calculate file positions for distribute mode (interleave evenly).
    /// Based on the shuffle-folder algorithm.
    /// </summary>
    private List<(string SourcePath, bool IsNew, int FinalIndex)> CalculateDistributedPositions(
        List<PhotoItem> existingPhotos,
        List<string> newFiles)
    {
        var operations = new List<(string, bool, int)>();
        var sortedExisting = existingPhotos.OrderBy(p => p.DisplayOrder).ToList();

        int oldCount = sortedExisting.Count;
        int newCount = newFiles.Count;
        int total = oldCount + newCount;

        // Calculate positions where new files should be inserted
        var insertPositions = new List<int>();
        for (int i = 0; i < newCount; i++)
        {
            // Distribute evenly: position = (i + 1) * total / (newCount + 1)
            int pos = (i + 1) * total / (newCount + 1);
            insertPositions.Add(pos);
        }

        // Build interleaved list
        int oldIdx = 0;
        int newIdx = 0;

        for (int finalIdx = 0; finalIdx < total; finalIdx++)
        {
            // Check if a new file goes at this position
            if (newIdx < insertPositions.Count && finalIdx == insertPositions[newIdx])
            {
                operations.Add((newFiles[newIdx], true, finalIdx));
                newIdx++;
            }
            else
            {
                operations.Add((sortedExisting[oldIdx].FilePath, false, finalIdx));
                oldIdx++;
            }
        }

        return operations;
    }
}
