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
    /// entry) stay at the front, followed by the photos grouped by tag in
    /// ascending order. Untagged photos sort as though their tag were 5.5, so
    /// they land between tag 5 and tag 6.
    /// Ordering within every group is stable, preserving the current
    /// <see cref="PhotoItem.DisplayOrder"/>, so sorting twice is a no-op.
    /// </summary>
    public static List<PhotoItem> SortByTag(IEnumerable<PhotoItem> photos)
    {
        return photos
            .OrderBy(p => GroupRank(p))
            .ThenBy(p => TagRank(p))
            .ThenBy(p => p.DisplayOrder)
            .ToList();
    }

    // 0 = folders (".." sorts first within this group via DisplayOrder),
    // 1 = photos, tagged or not.
    private static int GroupRank(PhotoItem photo) => photo.IsFolder ? 0 : 1;

    // Tags are doubled so the untagged slot (11) sits exactly halfway between
    // tag 5 (10) and tag 6 (12) without resorting to floating point.
    private static int TagRank(PhotoItem photo) => photo.Tag.HasValue ? photo.Tag.Value * 2 : 11;
}
