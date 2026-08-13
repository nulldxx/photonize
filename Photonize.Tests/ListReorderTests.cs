using Photonize.Helpers;
using Photonize.Models;
using Xunit;

namespace Photonize.Tests;

public class ListReorderTests
{
    private static PhotoItem Photo(string name) => new()
    {
        FilePath = @"C:\photos\" + name,
        FileName = name
    };

    private static PhotoItem Folder(string name) => new()
    {
        FilePath = @"C:\photos\" + name,
        FileName = name,
        IsFolder = true
    };

    private static string[] Names(IEnumerable<PhotoItem> items) =>
        items.Select(i => i.FileName).ToArray();

    [Fact]
    public void SendToFront_MovesItemToStartOfList()
    {
        var a = Photo("a.jpg");
        var b = Photo("b.jpg");
        var c = Photo("c.jpg");

        var result = ListReorder.SendTo(new[] { a, b, c }, new[] { c }, SendToEdge.Front);

        Assert.Equal(new[] { "c.jpg", "a.jpg", "b.jpg" }, Names(result));
    }

    [Fact]
    public void SendToFront_LandsAfterTheParentAndFolderTilesNotAtIndexZero()
    {
        var parent = Folder("..");
        var sub = Folder("holiday");
        var a = Photo("a.jpg");
        var b = Photo("b.jpg");

        var result = ListReorder.SendTo(new[] { parent, sub, a, b }, new[] { b }, SendToEdge.Front);

        Assert.Equal(new[] { "..", "holiday", "b.jpg", "a.jpg" }, Names(result));
    }

    [Fact]
    public void SendToBack_MovesItemToEndOfList()
    {
        var a = Photo("a.jpg");
        var b = Photo("b.jpg");
        var c = Photo("c.jpg");

        var result = ListReorder.SendTo(new[] { a, b, c }, new[] { a }, SendToEdge.Back);

        Assert.Equal(new[] { "b.jpg", "c.jpg", "a.jpg" }, Names(result));
    }

    [Fact]
    public void MultiSelect_KeepsRelativeOrderAtTheFront()
    {
        var parent = Folder("..");
        var a = Photo("a.jpg");
        var b = Photo("b.jpg");
        var c = Photo("c.jpg");
        var d = Photo("d.jpg");

        // Deliberately hand them over in reverse selection order.
        var result = ListReorder.SendTo(new[] { parent, a, b, c, d }, new[] { d, b }, SendToEdge.Front);

        Assert.Equal(new[] { "..", "b.jpg", "d.jpg", "a.jpg", "c.jpg" }, Names(result));
    }

    [Fact]
    public void MultiSelect_KeepsRelativeOrderAtTheBack()
    {
        var a = Photo("a.jpg");
        var b = Photo("b.jpg");
        var c = Photo("c.jpg");
        var d = Photo("d.jpg");

        var result = ListReorder.SendTo(new[] { a, b, c, d }, new[] { c, a }, SendToEdge.Back);

        Assert.Equal(new[] { "b.jpg", "d.jpg", "a.jpg", "c.jpg" }, Names(result));
    }

    [Fact]
    public void FolderTilesAreNeverMoved()
    {
        var parent = Folder("..");
        var sub = Folder("holiday");
        var a = Photo("a.jpg");

        var frontResult = ListReorder.SendTo(new[] { parent, sub, a }, new[] { sub }, SendToEdge.Front);
        var backResult = ListReorder.SendTo(new[] { parent, sub, a }, new[] { parent }, SendToEdge.Back);

        Assert.Equal(new[] { "..", "holiday", "a.jpg" }, Names(frontResult));
        Assert.Equal(new[] { "..", "holiday", "a.jpg" }, Names(backResult));
    }

    [Fact]
    public void MixedSelection_MovesOnlyTheMediaAndLeavesFoldersPut()
    {
        var parent = Folder("..");
        var a = Photo("a.jpg");
        var b = Photo("b.jpg");

        var result = ListReorder.SendTo(new[] { parent, a, b }, new[] { parent, b }, SendToEdge.Front);

        Assert.Equal(new[] { "..", "b.jpg", "a.jpg" }, Names(result));
    }

    [Fact]
    public void AlreadyAtTheEdgeIsANoOp()
    {
        var parent = Folder("..");
        var a = Photo("a.jpg");
        var b = Photo("b.jpg");
        var photos = new[] { parent, a, b };

        Assert.Equal(new[] { "..", "a.jpg", "b.jpg" }, Names(ListReorder.SendTo(photos, new[] { a }, SendToEdge.Front)));
        Assert.Equal(new[] { "..", "a.jpg", "b.jpg" }, Names(ListReorder.SendTo(photos, new[] { b }, SendToEdge.Back)));

        Assert.False(ListReorder.WouldChangeOrder(photos, new[] { a }, SendToEdge.Front));
        Assert.False(ListReorder.WouldChangeOrder(photos, new[] { b }, SendToEdge.Back));
        Assert.True(ListReorder.WouldChangeOrder(photos, new[] { b }, SendToEdge.Front));
    }

    [Fact]
    public void WouldChangeOrder_IsFalseForFolderOnlyAndEmptySelections()
    {
        var parent = Folder("..");
        var a = Photo("a.jpg");
        var photos = new[] { parent, a };

        Assert.False(ListReorder.WouldChangeOrder(photos, new[] { parent }, SendToEdge.Back));
        Assert.False(ListReorder.WouldChangeOrder(photos, Array.Empty<PhotoItem>(), SendToEdge.Front));
    }

    [Fact]
    public void SendToIsIdempotent()
    {
        var parent = Folder("..");
        var a = Photo("a.jpg");
        var b = Photo("b.jpg");
        var c = Photo("c.jpg");
        var photos = new[] { parent, a, b, c };

        var once = ListReorder.SendTo(photos, new[] { c }, SendToEdge.Front);
        var twice = ListReorder.SendTo(once, new[] { c }, SendToEdge.Front);

        Assert.Equal(Names(once), Names(twice));
        Assert.False(ListReorder.WouldChangeOrder(once, new[] { c }, SendToEdge.Front));
    }

    [Fact]
    public void SendToDoesNotMutateTheInputList()
    {
        var a = Photo("a.jpg");
        var b = Photo("b.jpg");
        var photos = new List<PhotoItem> { a, b };

        ListReorder.SendTo(photos, new[] { b }, SendToEdge.Front);

        Assert.Equal(new[] { "a.jpg", "b.jpg" }, Names(photos));
    }

    [Fact]
    public void FirstMediaIndex_IsListLengthWhenThereIsNoMedia()
    {
        var photos = new[] { Folder(".."), Folder("holiday") };

        Assert.Equal(2, ListReorder.FirstMediaIndex(photos));
    }
}
