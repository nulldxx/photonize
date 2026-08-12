using System.Collections;
using System.IO;
using System.Linq;
using Photonize.Models;

namespace Photonize.Helpers;

/// <summary>
/// Pure helpers for building the payload of a drag that may leave the application.
/// Deliberately free of WPF types so the rules that decide which files get handed to
/// Windows Explorer can be unit tested without a UI.
/// </summary>
public static class DragPayloadBuilder
{
    /// <summary>The synthetic tile that navigates up to the parent directory.</summary>
    public const string ParentNavigationName = "..";

    /// <summary>Extension of the sibling caption file that travels with a photo.</summary>
    public const string CaptionExtension = ".txt";

    /// <summary>
    /// True for the ".." tile. A real directory can never be named ".." on Windows,
    /// so matching on the name is safe.
    /// </summary>
    public static bool IsParentNavigationItem(PhotoItem? item) =>
        item != null && item.IsFolder && item.FileName == ParentNavigationName;

    /// <summary>
    /// Normalises the drag payload (a single PhotoItem, or a collection of them) into a
    /// list, dropping the ".." tile so a drag can never hand out the parent directory.
    /// </summary>
    public static List<PhotoItem> SelectDraggableItems(object? dragData)
    {
        var candidates = dragData switch
        {
            PhotoItem single => new List<PhotoItem> { single },
            string => new List<PhotoItem>(),
            IEnumerable sequence => sequence.OfType<PhotoItem>().ToList(),
            _ => new List<PhotoItem>()
        };

        return candidates
            .Where(item => !IsParentNavigationItem(item) && !string.IsNullOrWhiteSpace(item.FilePath))
            .ToList();
    }

    /// <summary>
    /// Absolute paths to advertise to the shell: each dragged item, plus each photo's
    /// sibling .txt caption so the caption travels with the image. Order is preserved,
    /// paths are de-duplicated case-insensitively (two photos can share a caption stem)
    /// and only paths that exist on disk are included.
    /// </summary>
    public static List<string> BuildFileDropPaths(IEnumerable<PhotoItem> items)
    {
        var paths = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string path)
        {
            if (seen.Add(path))
                paths.Add(path);
        }

        foreach (var item in items)
        {
            if (IsParentNavigationItem(item) || string.IsNullOrWhiteSpace(item.FilePath))
                continue;

            if (item.IsFolder)
            {
                // A folder carries its own contents; there is no caption to look for.
                if (Directory.Exists(item.FilePath))
                    Add(item.FilePath);
                continue;
            }

            if (!File.Exists(item.FilePath))
                continue;

            Add(item.FilePath);

            var caption = GetCaptionPath(item.FilePath);
            if (caption != null && File.Exists(caption))
                Add(caption);
        }

        return paths;
    }

    /// <summary>
    /// Sibling .txt caption for a media file, or null when the path is already a .txt
    /// (which would otherwise nominate itself as its own caption).
    /// </summary>
    public static string? GetCaptionPath(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return null;

        var caption = Path.ChangeExtension(filePath, CaptionExtension);
        return string.Equals(caption, filePath, StringComparison.OrdinalIgnoreCase) ? null : caption;
    }
}
