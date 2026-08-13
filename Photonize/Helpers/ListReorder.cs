using System.Collections.Generic;
using System.Linq;
using Photonize.Models;

namespace Photonize.Helpers;

/// <summary>Which end of the list an item is being sent to.</summary>
public enum SendToEdge
{
    Front,
    Back
}

/// <summary>
/// Pure list-position arithmetic for the "Send to -> Front/Back" commands.
/// Deliberately free of WPF types, matching <see cref="DragPayloadBuilder"/>, so
/// <c>Photonize.Tests</c> can exercise the rules without a UI.
/// </summary>
public static class ListReorder
{
    /// <summary>
    /// Returns a new ordering with <paramref name="moving"/> collected at one end of
    /// the list. Folder tiles (including "..") are never moved and always keep their
    /// leading block, so "Front" means the first *media* slot rather than index 0.
    /// The relative order of the moved items is preserved.
    /// </summary>
    public static List<PhotoItem> SendTo(
        IReadOnlyList<PhotoItem> photos,
        IReadOnlyCollection<PhotoItem> moving,
        SendToEdge edge)
    {
        if (photos == null)
            return new List<PhotoItem>();

        var result = photos.ToList();
        if (moving == null || moving.Count == 0)
            return result;

        // Folders anchor the top of the grid; they are never a legal move target.
        var movingSet = new HashSet<PhotoItem>(moving.Where(p => p != null && !p.IsFolder));
        if (movingSet.Count == 0)
            return result;

        // Keep the moved items in the order they appear now, not the order they
        // happened to be selected in.
        var moved = result.Where(movingSet.Contains).ToList();
        if (moved.Count == 0)
            return result;

        var remainder = result.Where(p => !movingSet.Contains(p)).ToList();

        var insertAt = edge == SendToEdge.Front
            ? FirstMediaIndex(remainder)
            : remainder.Count;

        remainder.InsertRange(insertAt, moved);
        return remainder;
    }

    /// <summary>
    /// True when <see cref="SendTo"/> would actually change the ordering, so the
    /// commands can disable themselves rather than raise a spurious unsaved-changes flag.
    /// </summary>
    public static bool WouldChangeOrder(
        IReadOnlyList<PhotoItem> photos,
        IReadOnlyCollection<PhotoItem> moving,
        SendToEdge edge)
    {
        if (photos == null || moving == null || moving.Count == 0)
            return false;

        var reordered = SendTo(photos, moving, edge);
        return !reordered.SequenceEqual(photos);
    }

    /// <summary>
    /// Index of the first non-folder tile — the slot "Front" targets. Load order puts
    /// the ".." tile first, then folders, then media, so this is the end of that
    /// leading block (or the list length when there is no media at all).
    /// </summary>
    public static int FirstMediaIndex(IReadOnlyList<PhotoItem> photos)
    {
        if (photos == null)
            return 0;

        int index = 0;
        while (index < photos.Count && photos[index].IsFolder)
            index++;

        return index;
    }
}
