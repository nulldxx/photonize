using Photonize.Helpers;
using Photonize.Models;
using Xunit;

namespace Photonize.Tests;

public class VideoSelectionRulesTests
{
    // PhotoItem.IsVideo is derived from the extension, so the file name alone
    // decides whether a tile counts as a video.
    private static PhotoItem Tile(string name) => new()
    {
        FilePath = @"C:\v\" + name,
        FileName = name
    };

    private static PhotoItem Video(string name) => Tile(name);

    private static PhotoItem Image(string name) => Tile(name);

    private static PhotoItem Folder(string name) => new()
    {
        FilePath = @"C:\v\" + name,
        FileName = name,
        IsFolder = true
    };

    private static PhotoItem Parent() => new()
    {
        FilePath = @"C:\",
        FileName = "..",
        IsFolder = true
    };

    private static string[] Names(IEnumerable<PhotoItem> items) =>
        items.Select(i => i.FileName).ToArray();

    private static List<PhotoItem> None() => new();

    [Fact]
    public void ClickingAVideoOutsideTheSelectionActsOnThatVideoAlone()
    {
        var a = Video("a.mp4");
        var b = Video("b.mp4");
        var grid = new List<PhotoItem> { a, b };

        var targets = VideoSelectionRules.ResolveTargets(b, new List<PhotoItem> { a }, grid);

        Assert.Equal(new[] { "b.mp4" }, Names(targets));
    }

    [Fact]
    public void ClickingAVideoInsideTheSelectionActsOnTheWholeSelection()
    {
        var a = Video("a.mp4");
        var b = Video("b.mp4");
        var grid = new List<PhotoItem> { a, b };

        var targets = VideoSelectionRules.ResolveTargets(b, new List<PhotoItem> { a, b }, grid);

        Assert.Equal(new[] { "a.mp4", "b.mp4" }, Names(targets));
    }

    [Fact]
    public void TargetsFollowGridOrderNotSelectionOrder()
    {
        // WPF's SelectedItems is ordered by when each item joined the selection,
        // so ctrl-clicking b then a must still splice a before b.
        var a = Video("a.mp4");
        var b = Video("b.mp4");
        var c = Video("c.mp4");
        var grid = new List<PhotoItem> { a, b, c };

        var targets = VideoSelectionRules.ResolveTargets(c, new List<PhotoItem> { c, a, b }, grid);

        Assert.Equal(new[] { "a.mp4", "b.mp4", "c.mp4" }, Names(targets));
    }

    [Fact]
    public void FoldersImagesAndTheParentTileAreDropped()
    {
        var parent = Parent();
        var folder = Folder("sub");
        var image = Image("photo.jpg");
        var video = Video("a.mp4");
        var grid = new List<PhotoItem> { parent, folder, image, video };

        var targets = VideoSelectionRules.ResolveTargets(null, grid, grid);

        Assert.Equal(new[] { "a.mp4" }, Names(targets));
    }

    [Fact]
    public void AMixedSelectionSplicesOnlyItsVideos()
    {
        var image = Image("photo.jpg");
        var a = Video("a.mp4");
        var b = Video("b.mov");
        var grid = new List<PhotoItem> { image, a, b };

        var targets = VideoSelectionRules.ResolveTargets(a, grid, grid);

        Assert.Equal(new[] { "a.mp4", "b.mov" }, Names(targets));
    }

    [Fact]
    public void ClickingAFolderYieldsNothing()
    {
        var folder = Folder("sub");
        var video = Video("a.mp4");
        var grid = new List<PhotoItem> { folder, video };

        Assert.Empty(VideoSelectionRules.ResolveTargets(folder, None(), grid));
    }

    [Fact]
    public void ClickingTheParentTileYieldsNothing()
    {
        var parent = Parent();
        var grid = new List<PhotoItem> { parent };

        Assert.Empty(VideoSelectionRules.ResolveTargets(parent, None(), grid));
    }

    [Fact]
    public void ClickingAnImageYieldsNothing()
    {
        var image = Image("photo.jpg");
        var grid = new List<PhotoItem> { image };

        Assert.Empty(VideoSelectionRules.ResolveTargets(image, None(), grid));
    }

    [Fact]
    public void TheSameFileSelectedTwiceIsCountedOnce()
    {
        // Two PhotoItems can point at one path after a refresh races a selection.
        var a = Video("a.mp4");
        var duplicate = Video("A.MP4");
        var grid = new List<PhotoItem> { a, duplicate };

        var targets = VideoSelectionRules.ResolveTargets(a, grid, grid);

        Assert.Single(targets);
    }

    [Fact]
    public void OneVideoIsNotEnoughToSplice()
    {
        var image = Image("photo.jpg");
        var video = Video("a.mp4");
        var grid = new List<PhotoItem> { image, video };

        // The CanExecute gate is >= 2, so a lone video leaves Splice disabled.
        Assert.True(VideoSelectionRules.ResolveTargets(video, grid, grid).Count < 2);
    }

    [Fact]
    public void SliceAcceptsSeveralSelectedVideos()
    {
        var a = Video("a.mp4");
        var b = Video("b.mp4");
        var grid = new List<PhotoItem> { a, b };

        var targets = VideoSelectionRules.ResolveTargets(a, grid, grid);

        Assert.Equal(new[] { "a.mp4", "b.mp4" }, Names(targets));
    }

    [Fact]
    public void NothingClickedFallsBackToTheSelection()
    {
        var a = Video("a.mp4");
        var grid = new List<PhotoItem> { a };

        var targets = VideoSelectionRules.ResolveTargets(null, new List<PhotoItem> { a }, grid);

        Assert.Equal(new[] { "a.mp4" }, Names(targets));
    }

    [Fact]
    public void AnItemMissingFromTheGridStillSortsLastRatherThanThrowing()
    {
        var a = Video("a.mp4");
        var stale = Video("stale.mp4");

        var targets = VideoSelectionRules.ResolveTargets(
            null, new List<PhotoItem> { stale, a }, new List<PhotoItem> { a });

        Assert.Equal(new[] { "a.mp4", "stale.mp4" }, Names(targets));
    }
}
