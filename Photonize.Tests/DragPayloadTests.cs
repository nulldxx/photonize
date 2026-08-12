using Photonize.Helpers;
using Photonize.Models;
using Xunit;

namespace Photonize.Tests;

public class BuildFileDropPathsTests : IDisposable
{
    private readonly string _directory;

    public BuildFileDropPathsTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "photonize-drag-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }
        catch
        {
            // Best effort temp cleanup
        }
    }

    private string Touch(string fileName)
    {
        var path = Path.Combine(_directory, fileName);
        File.WriteAllText(path, "x");
        return path;
    }

    private PhotoItem Photo(string fileName) => new()
    {
        FilePath = Path.Combine(_directory, fileName),
        FileName = fileName
    };

    private PhotoItem Folder(string name) => new()
    {
        FilePath = Path.Combine(_directory, name),
        FileName = name,
        IsFolder = true
    };

    [Fact]
    public void IncludesPhotoAndItsCaption()
    {
        var image = Touch("a.jpg");
        var caption = Touch("a.txt");

        var paths = DragPayloadBuilder.BuildFileDropPaths(new[] { Photo("a.jpg") });

        Assert.Equal(new[] { image, caption }, paths);
    }

    [Fact]
    public void OmitsCaption_WhenItDoesNotExist()
    {
        var image = Touch("a.jpg");

        var paths = DragPayloadBuilder.BuildFileDropPaths(new[] { Photo("a.jpg") });

        Assert.Equal(new[] { image }, paths);
    }

    [Fact]
    public void DeDupesACaptionSharedByTwoPhotos()
    {
        var jpg = Touch("a.jpg");
        var png = Touch("a.png");
        var caption = Touch("a.txt");

        var paths = DragPayloadBuilder.BuildFileDropPaths(new[] { Photo("a.jpg"), Photo("a.png") });

        Assert.Equal(new[] { jpg, caption, png }, paths);
    }

    [Fact]
    public void SkipsFilesThatNoLongerExist()
    {
        var present = Touch("here.jpg");

        var paths = DragPayloadBuilder.BuildFileDropPaths(new[] { Photo("gone.jpg"), Photo("here.jpg") });

        Assert.Equal(new[] { present }, paths);
    }

    [Fact]
    public void IncludesFolders_WithoutProbingForCaptions()
    {
        var sub = Path.Combine(_directory, "sub");
        Directory.CreateDirectory(sub);
        Touch("sub.txt");

        var paths = DragPayloadBuilder.BuildFileDropPaths(new[] { Folder("sub") });

        Assert.Equal(new[] { sub }, paths);
    }

    [Fact]
    public void SkipsFoldersThatNoLongerExist()
    {
        var paths = DragPayloadBuilder.BuildFileDropPaths(new[] { Folder("missing") });

        Assert.Empty(paths);
    }

    [Fact]
    public void ExcludesTheParentNavigationItem()
    {
        // The ".." tile points at the parent directory. Handing that to Explorer would
        // move the whole parent folder, so it must never reach the drop list.
        var parent = new PhotoItem
        {
            FilePath = Directory.GetParent(_directory)!.FullName,
            FileName = "..",
            IsFolder = true
        };

        var paths = DragPayloadBuilder.BuildFileDropPaths(new[] { parent });

        Assert.Empty(paths);
    }

    [Fact]
    public void PreservesTheOrderOfTheDraggedItems()
    {
        var c = Touch("c.jpg");
        var a = Touch("a.jpg");
        var b = Touch("b.jpg");

        var paths = DragPayloadBuilder.BuildFileDropPaths(
            new[] { Photo("c.jpg"), Photo("a.jpg"), Photo("b.jpg") });

        Assert.Equal(new[] { c, a, b }, paths);
    }

    [Fact]
    public void ReturnsEmpty_ForAnEmptySelection()
    {
        Assert.Empty(DragPayloadBuilder.BuildFileDropPaths(Array.Empty<PhotoItem>()));
    }
}

public class SelectDraggableItemsTests
{
    private static PhotoItem Photo(string name) => new()
    {
        FilePath = @"C:\photos\" + name,
        FileName = name
    };

    private static PhotoItem ParentTile() => new()
    {
        FilePath = @"C:\",
        FileName = "..",
        IsFolder = true
    };

    [Fact]
    public void ReturnsSingleItem_WhenGivenOnePhotoItem()
    {
        var items = DragPayloadBuilder.SelectDraggableItems(Photo("a.jpg"));

        Assert.Single(items);
        Assert.Equal("a.jpg", items[0].FileName);
    }

    [Fact]
    public void ReturnsAllItems_WhenGivenACollection()
    {
        var payload = new List<PhotoItem> { Photo("a.jpg"), Photo("b.jpg") };

        var items = DragPayloadBuilder.SelectDraggableItems(payload);

        Assert.Equal(new[] { "a.jpg", "b.jpg" }, items.Select(i => i.FileName));
    }

    [Fact]
    public void FiltersTheParentTileOutOfAMixedSelection()
    {
        var payload = new List<PhotoItem> { ParentTile(), Photo("a.jpg"), Photo("b.jpg") };

        var items = DragPayloadBuilder.SelectDraggableItems(payload);

        Assert.Equal(new[] { "a.jpg", "b.jpg" }, items.Select(i => i.FileName));
    }

    [Fact]
    public void ReturnsEmpty_WhenOnlyTheParentTileIsDragged()
    {
        Assert.Empty(DragPayloadBuilder.SelectDraggableItems(ParentTile()));
    }

    [Fact]
    public void IgnoresItemsWithABlankFilePath()
    {
        var payload = new List<PhotoItem> { new() { FileName = "orphan.jpg" }, Photo("a.jpg") };

        var items = DragPayloadBuilder.SelectDraggableItems(payload);

        Assert.Equal(new[] { "a.jpg" }, items.Select(i => i.FileName));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("some string")]
    [InlineData(42)]
    public void ReturnsEmpty_ForUnrelatedPayloads(object? payload)
    {
        Assert.Empty(DragPayloadBuilder.SelectDraggableItems(payload));
    }

    [Fact]
    public void ReturnsEmpty_ForACollectionOfNonPhotoItems()
    {
        Assert.Empty(DragPayloadBuilder.SelectDraggableItems(new List<string> { "a", "b" }));
    }
}

public class ParentNavigationItemTests
{
    // This predicate guards a recursive directory delete (MainViewModel.DeletePhoto)
    // as well as the drag payload, so it is worth pinning down directly.

    [Fact]
    public void IsTrue_ForTheParentTile()
    {
        var tile = new PhotoItem { FilePath = @"C:\photos", FileName = "..", IsFolder = true };

        Assert.True(DragPayloadBuilder.IsParentNavigationItem(tile));
    }

    [Fact]
    public void IsFalse_ForARealFolder()
    {
        var folder = new PhotoItem { FilePath = @"C:\photos\sub", FileName = "sub", IsFolder = true };

        Assert.False(DragPayloadBuilder.IsParentNavigationItem(folder));
    }

    [Fact]
    public void IsFalse_ForAFileOddlyNamedDotDot()
    {
        var file = new PhotoItem { FilePath = @"C:\photos\..", FileName = "..", IsFolder = false };

        Assert.False(DragPayloadBuilder.IsParentNavigationItem(file));
    }

    [Fact]
    public void IsFalse_ForNull()
    {
        Assert.False(DragPayloadBuilder.IsParentNavigationItem(null));
    }
}

public class CaptionPathTests
{
    [Fact]
    public void ReturnsTheSiblingTxt()
    {
        Assert.Equal(@"C:\photos\a.txt", DragPayloadBuilder.GetCaptionPath(@"C:\photos\a.jpg"));
    }

    [Fact]
    public void ReturnsNull_ForAPathThatIsAlreadyTxt()
    {
        Assert.Null(DragPayloadBuilder.GetCaptionPath(@"C:\photos\a.txt"));
    }

    [Fact]
    public void IsCaseInsensitiveAboutAnExistingTxtExtension()
    {
        Assert.Null(DragPayloadBuilder.GetCaptionPath(@"C:\photos\a.TXT"));
    }

    [Fact]
    public void ReturnsNull_ForABlankPath()
    {
        Assert.Null(DragPayloadBuilder.GetCaptionPath(string.Empty));
    }
}
