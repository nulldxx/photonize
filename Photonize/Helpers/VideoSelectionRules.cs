using Photonize.Models;

namespace Photonize.Helpers;

/// <summary>
/// Works out which tiles Slice and Splice act on.
///
/// A right-click does not change the WPF selection, so the clicked tile is only
/// treated as part of a multi-item operation when it is actually inside the
/// current selection; otherwise it acts alone. That is the rule
/// <c>MainViewModel.ResolveItemsToSend</c> and <c>ResolveItemsToDelete</c>
/// already follow, and it is lifted here so it can be unit-tested.
/// </summary>
public static class VideoSelectionRules
{
    /// <summary>
    /// Every video in <paramref name="candidates"/>, in grid order, with folders,
    /// the ".." tile, stills and raw files dropped and duplicates removed.
    /// </summary>
    private static List<PhotoItem> VideosOnly(IEnumerable<PhotoItem?> candidates, IReadOnlyList<PhotoItem> gridOrder)
    {
        var position = new Dictionary<PhotoItem, int>();
        for (var index = 0; index < gridOrder.Count; index++)
        {
            position.TryAdd(gridOrder[index], index);
        }

        return candidates
            .Where(item => item is { IsFolder: false, IsVideo: true })
            .Select(item => item!)
            .Where(item => !DragPayloadBuilder.IsParentNavigationItem(item))
            .Where(item => !string.IsNullOrWhiteSpace(item.FilePath))
            .DistinctBy(item => item.FilePath, StringComparer.OrdinalIgnoreCase)
            // WPF's SelectedItems is ordered by when each item joined the
            // selection, which is unpredictable after ctrl-clicking around. Grid
            // order is what the user can actually see, and it is what Apply
            // Rename numbers by, so it is the only defensible splice order.
            .OrderBy(item => position.TryGetValue(item, out var index) ? index : int.MaxValue)
            .ToList();
    }

    /// <summary>
    /// The clips an operation acts on, in grid order.
    ///
    /// Slice and Splice resolve targets identically; they differ only in how many
    /// they need, which each command's CanExecute decides (Slice needs one,
    /// Splice two).
    /// </summary>
    public static List<PhotoItem> ResolveTargets(
        PhotoItem? clicked,
        IReadOnlyList<PhotoItem> selection,
        IReadOnlyList<PhotoItem> gridOrder)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(gridOrder);

        return VideosOnly(Candidates(clicked, selection), gridOrder);
    }

    private static IEnumerable<PhotoItem?> Candidates(PhotoItem? clicked, IReadOnlyList<PhotoItem> selection)
    {
        if (clicked != null && selection.Contains(clicked))
            return selection;

        if (clicked != null)
            return new[] { clicked };

        return selection;
    }

    /// <summary>
    /// The single clip Extract clip acts on.
    ///
    /// Trimming several clips at once would mean applying one range to all of
    /// them, which is meaningless, so this resolves to exactly one or to nothing.
    /// A right-clicked video always wins -- even inside a larger selection, where
    /// "which clip?" would otherwise have no answer. A right-click on anything
    /// else resolves to nothing rather than falling through to the selection.
    /// Only with no clicked tile at all does the selection decide, and then only
    /// when it holds exactly one video.
    /// </summary>
    public static PhotoItem? ResolveSingleTarget(
        PhotoItem? clicked,
        IReadOnlyList<PhotoItem> selection,
        IReadOnlyList<PhotoItem> gridOrder)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(gridOrder);

        // A clicked tile settles it either way. If it is not a clip the answer is
        // "none" rather than the selection's clip: the click named a target, and
        // quietly trimming something else is the worst possible reading of it.
        if (clicked != null)
        {
            var clickedVideo = VideosOnly(new[] { clicked }, gridOrder);
            return clickedVideo.Count == 1 ? clickedVideo[0] : null;
        }

        var selected = VideosOnly(selection, gridOrder);
        return selected.Count == 1 ? selected[0] : null;
    }
}
