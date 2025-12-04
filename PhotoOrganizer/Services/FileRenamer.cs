using System.IO;
using System.Text.RegularExpressions;
using PhotoOrganizer.Models;

namespace PhotoOrganizer.Services;

public class FileRenamer
{
    public bool ValidatePrefix(string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
            return false;

        // Check for invalid filename characters
        var invalidChars = Path.GetInvalidFileNameChars();
        return !prefix.Any(c => invalidChars.Contains(c));
    }

    public async Task<(bool Success, string Message)> RenameFilesAsync(List<PhotoItem> photos, string prefix)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (!ValidatePrefix(prefix))
                    return (false, "Invalid prefix. Please use only valid filename characters.");

                if (photos.Count == 0)
                    return (false, "No photos to rename.");

                // Sort by display order
                var sortedPhotos = photos.OrderBy(p => p.DisplayOrder).ToList();

                // Prepare rename operations
                var renameOperations = new List<(string OldPath, string NewPath, PhotoItem Item)>();

                for (int i = 0; i < sortedPhotos.Count; i++)
                {
                    var photo = sortedPhotos[i];
                    var extension = Path.GetExtension(photo.FilePath);
                    var directory = Path.GetDirectoryName(photo.FilePath);

                    if (string.IsNullOrEmpty(directory))
                        continue;

                    // Generate new filename: prefix-00001.ext, prefix-00002.ext, etc.
                    var newFileName = $"{prefix}-{(i + 1):D5}{extension}";
                    var newPath = Path.Combine(directory, newFileName);

                    // Check if the new name would conflict with an existing file
                    // that's not in our rename list
                    if (File.Exists(newPath) && !sortedPhotos.Any(p => p.FilePath == newPath))
                    {
                        return (false, $"File already exists: {newFileName}. Please choose a different prefix.");
                    }

                    renameOperations.Add((photo.FilePath, newPath, photo));
                }

                // Use temporary names to avoid conflicts during rename
                var tempOperations = new List<(string TempPath, string FinalPath, PhotoItem Item)>();

                // First pass: rename to temporary names
                foreach (var (oldPath, newPath, item) in renameOperations)
                {
                    if (oldPath == newPath)
                        continue; // Already has the correct name

                    var tempPath = oldPath + ".tmp_rename";
                    File.Move(oldPath, tempPath);
                    tempOperations.Add((tempPath, newPath, item));
                }

                // Second pass: rename from temporary to final names
                foreach (var (tempPath, finalPath, item) in tempOperations)
                {
                    File.Move(tempPath, finalPath);
                    item.FilePath = finalPath;
                    item.FileName = Path.GetFileName(finalPath);
                }

                return (true, $"Successfully renamed {tempOperations.Count} file(s).");
            }
            catch (Exception ex)
            {
                return (false, $"Error during rename: {ex.Message}");
            }
        });
    }

    public int GetNextAvailableNumber(string directory, string prefix)
    {
        var pattern = new Regex($@"^{Regex.Escape(prefix)}-(\d{{5}})\.(.+)$");
        var existingNumbers = new HashSet<int>();

        if (!Directory.Exists(directory))
            return 1;

        foreach (var file in Directory.GetFiles(directory))
        {
            var fileName = Path.GetFileName(file);
            var match = pattern.Match(fileName);
            if (match.Success && int.TryParse(match.Groups[1].Value, out int number))
            {
                existingNumbers.Add(number);
            }
        }

        int nextNumber = 1;
        while (existingNumbers.Contains(nextNumber))
        {
            nextNumber++;
        }

        return nextNumber;
    }
}
