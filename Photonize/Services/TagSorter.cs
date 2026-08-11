using Photonize.Models;

namespace Photonize.Services;

/// <summary>
/// Pure ordering logic for "Sort by tag". Kept free of WPF and file I/O so it
/// can be exercised directly by unit tests.
/// </summary>
public static class TagSorter
{
    /// <summary>
    /// Returns the photos reordered so that folders (including the ".." parent
    /// entry) stay at the front, followed by tagged photos grouped by tag in
    /// ascending order, followed by untagged photos.
    /// Ordering within every group is stable, preserving the current
    /// <see cref="PhotoItem.DisplayOrder"/>, so sorting twice is a no-op.
    /// </summary>
    public static List<PhotoItem> SortByTag(IEnumerable<PhotoItem> photos)
    {
        return photos
            .OrderBy(p => GroupRank(p))
            .ThenBy(p => p.Tag ?? 0)
            .ThenBy(p => p.DisplayOrder)
            .ToList();
    }

    // 0 = folders (".." sorts first within this group via DisplayOrder),
    // 1 = tagged photos, 2 = untagged photos.
    private static int GroupRank(PhotoItem photo)
    {
        if (photo.IsFolder)
            return 0;

        return photo.Tag.HasValue ? 1 : 2;
    }
}
