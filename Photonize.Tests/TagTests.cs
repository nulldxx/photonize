using Photonize.Models;
using Photonize.Services;
using Xunit;

namespace Photonize.Tests;

public class TagServiceTests : IDisposable
{
    private readonly string _directory;
    private readonly TagService _service = new();

    public TagServiceTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "photonize-tags-" + Guid.NewGuid().ToString("N"));
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

    private PhotoItem Photo(string fileName, int? tag = null) => new()
    {
        FilePath = Path.Combine(_directory, fileName),
        FileName = fileName,
        Tag = tag
    };

    [Fact]
    public void Save_ThenLoad_RoundTripsTags()
    {
        var photos = new List<PhotoItem>
        {
            Photo("a.jpg", 3),
            Photo("b.jpg", 0),
            Photo("c.jpg")
        };

        _service.Save(_directory, photos);
        var loaded = _service.Load(_directory);

        Assert.Equal(2, loaded.Count);
        Assert.Equal(3, loaded["a.jpg"]);
        Assert.Equal(0, loaded["b.jpg"]);
        Assert.False(loaded.ContainsKey("c.jpg"));
    }

    [Fact]
    public void Load_IsCaseInsensitive()
    {
        _service.Save(_directory, new List<PhotoItem> { Photo("Photo.JPG", 7) });

        var loaded = _service.Load(_directory);

        Assert.Equal(7, loaded["photo.jpg"]);
    }

    [Fact]
    public void Save_WithNoTags_DeletesTheFile()
    {
        var path = TagService.GetTagFilePath(_directory);

        _service.Save(_directory, new List<PhotoItem> { Photo("a.jpg", 1) });
        Assert.True(File.Exists(path));

        _service.Save(_directory, new List<PhotoItem> { Photo("a.jpg") });
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Save_IgnoresFolders()
    {
        var folder = Photo("subfolder", 4);
        folder.IsFolder = true;

        _service.Save(_directory, new List<PhotoItem> { folder });

        Assert.False(File.Exists(TagService.GetTagFilePath(_directory)));
    }

    [Fact]
    public void Load_ReturnsEmpty_WhenFileMissing()
    {
        Assert.Empty(_service.Load(_directory));
    }

    [Fact]
    public void Load_ReturnsEmpty_WhenFileIsCorrupt()
    {
        File.WriteAllText(TagService.GetTagFilePath(_directory), "not json {{{");

        Assert.Empty(_service.Load(_directory));
    }

    [Fact]
    public void Load_SkipsOutOfRangeTags()
    {
        File.WriteAllText(
            TagService.GetTagFilePath(_directory),
            "{\"a.jpg\": 3, \"b.jpg\": 42, \"c.jpg\": -1}");

        var loaded = _service.Load(_directory);

        Assert.Single(loaded);
        Assert.Equal(3, loaded["a.jpg"]);
    }
}

public class TagSorterTests
{
    private static PhotoItem Item(string name, int order, int? tag = null, bool isFolder = false) => new()
    {
        FilePath = @"C:\photos\" + name,
        FileName = name,
        DisplayOrder = order,
        Tag = tag,
        IsFolder = isFolder
    };

    [Fact]
    public void SortByTag_PutsFoldersFirst_ThenTagged_WithUntaggedBetween5And6()
    {
        var photos = new List<PhotoItem>
        {
            Item("untagged.jpg", 0),
            Item("tag2.jpg", 1, 2),
            Item("..", 2, isFolder: true),
            Item("tag1.jpg", 3, 1),
            Item("subfolder", 4, isFolder: true)
        };

        var sorted = TagSorter.SortByTag(photos);

        Assert.Equal(
            new[] { "..", "subfolder", "tag1.jpg", "tag2.jpg", "untagged.jpg" },
            sorted.Select(p => p.FileName));
    }

    [Fact]
    public void SortByTag_PlacesUntaggedBetweenTag5AndTag6()
    {
        var photos = new List<PhotoItem>
        {
            Item("tag6.jpg", 0, 6),
            Item("untagged.jpg", 1),
            Item("tag5.jpg", 2, 5),
            Item("tag0.jpg", 3, 0),
            Item("tag9.jpg", 4, 9)
        };

        var sorted = TagSorter.SortByTag(photos);

        Assert.Equal(
            new[] { "tag0.jpg", "tag5.jpg", "untagged.jpg", "tag6.jpg", "tag9.jpg" },
            sorted.Select(p => p.FileName));
    }

    [Fact]
    public void SortByTag_KeepsUntaggedPhotosTogetherInDisplayOrder()
    {
        var photos = new List<PhotoItem>
        {
            Item("second-untagged.jpg", 0),
            Item("tag6.jpg", 1, 6),
            Item("first-untagged.jpg", 2),
            Item("tag5.jpg", 3, 5)
        };
        photos[0].DisplayOrder = 7;
        photos[2].DisplayOrder = 3;

        var sorted = TagSorter.SortByTag(photos);

        Assert.Equal(
            new[] { "tag5.jpg", "first-untagged.jpg", "second-untagged.jpg", "tag6.jpg" },
            sorted.Select(p => p.FileName));
    }

    [Fact]
    public void SortByTag_GroupsTagsInAscendingOrder()
    {
        var photos = new List<PhotoItem>
        {
            Item("c.jpg", 0, 9),
            Item("a.jpg", 1, 0),
            Item("b.jpg", 2, 5)
        };

        var sorted = TagSorter.SortByTag(photos);

        Assert.Equal(new[] { 0, 5, 9 }, sorted.Select(p => p.Tag!.Value));
    }

    [Fact]
    public void SortByTag_IsStableWithinAGroup()
    {
        var photos = new List<PhotoItem>
        {
            Item("second.jpg", 5, 1),
            Item("first.jpg", 2, 1),
            Item("third.jpg", 9, 1)
        };

        var sorted = TagSorter.SortByTag(photos);

        Assert.Equal(
            new[] { "first.jpg", "second.jpg", "third.jpg" },
            sorted.Select(p => p.FileName));
    }

    [Fact]
    public void SortByTag_IsIdempotent_WhenDisplayOrderIsReapplied()
    {
        var photos = new List<PhotoItem>
        {
            Item("untagged.jpg", 0),
            Item("tag3.jpg", 1, 3),
            Item("tag1.jpg", 2, 1)
        };

        var first = TagSorter.SortByTag(photos);
        for (int i = 0; i < first.Count; i++)
        {
            first[i].DisplayOrder = i;
        }

        var second = TagSorter.SortByTag(first);

        Assert.Equal(first.Select(p => p.FileName), second.Select(p => p.FileName));
    }
}
