using System.IO;
using Newtonsoft.Json;
using Photonize.Models;

namespace Photonize.Services;

/// <summary>
/// Persists quick-tags for a photo directory in a sidecar JSON file.
/// Entries are keyed by filename rather than full path so the folder can be
/// moved or copied without losing its tags.
/// </summary>
public class TagService
{
    public const string TagFileName = ".photonize-tags.json";

    public static string GetTagFilePath(string directory) => Path.Combine(directory, TagFileName);

    public Dictionary<string, int> Load(string directory)
    {
        var tags = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrEmpty(directory))
            return tags;

        try
        {
            var path = GetTagFilePath(directory);
            if (!File.Exists(path))
                return tags;

            var json = File.ReadAllText(path);
            var loaded = JsonConvert.DeserializeObject<Dictionary<string, int>>(json);

            if (loaded != null)
            {
                foreach (var entry in loaded)
                {
                    // Ignore anything outside the 0-9 range we can actually display
                    if (entry.Value >= 0 && entry.Value <= 9)
                    {
                        tags[entry.Key] = entry.Value;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading tags: {ex.Message}");
        }

        return tags;
    }

    public void Save(string directory, IEnumerable<PhotoItem> photos)
    {
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            return;

        try
        {
            var path = GetTagFilePath(directory);

            var tags = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var photo in photos)
            {
                if (photo.IsFolder || !photo.Tag.HasValue)
                    continue;

                var fileName = Path.GetFileName(photo.FilePath);
                if (!string.IsNullOrEmpty(fileName))
                {
                    tags[fileName] = photo.Tag.Value;
                }
            }

            if (tags.Count == 0)
            {
                // Nothing tagged any more - don't leave an empty file lying around
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
                return;
            }

            var json = JsonConvert.SerializeObject(tags, Formatting.Indented);
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving tags: {ex.Message}");
        }
    }
}
