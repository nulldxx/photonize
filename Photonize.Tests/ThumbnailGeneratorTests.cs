using Photonize.Models;
using Photonize.Services;
using Xunit;

namespace Photonize.Tests;

/// <summary>
/// The format whitelist. These are static, pure predicates, so they need no dispatcher
/// — unlike the decode methods, which hand back WPF bitmaps.
/// </summary>
public class ThumbnailGeneratorTests
{
    [Theory]
    [InlineData(@"C:\photos\a.jpg")]
    [InlineData(@"C:\photos\a.jpeg")]
    [InlineData(@"C:\photos\a.png")]
    [InlineData(@"C:\photos\a.webp")]
    [InlineData(@"C:\photos\a.tif")]
    [InlineData(@"C:\photos\a.dng")]
    [InlineData(@"C:\photos\a.DNG")]
    public void RecognisesImages(string path)
    {
        Assert.True(ThumbnailGenerator.IsImageFile(path));
        Assert.False(ThumbnailGenerator.IsVideoFile(path));
    }

    [Theory]
    [InlineData(@"C:\photos\a.dng")]
    [InlineData(@"C:\photos\a.DnG")]
    public void RecognisesRaw(string path)
    {
        Assert.True(ThumbnailGenerator.IsRawFile(path));
        // Raw files are still images: they belong in the grid and in a rename.
        Assert.True(ThumbnailGenerator.IsImageFile(path));
    }

    [Theory]
    [InlineData(@"C:\photos\a.jpg")]
    [InlineData(@"C:\photos\a.tiff")]
    [InlineData(@"C:\photos\a.mp4")]
    // Other vendors' raw formats are not claimed — we only read DNG's preview layout.
    [InlineData(@"C:\photos\a.cr2")]
    [InlineData(@"C:\photos\a.nef")]
    [InlineData(@"C:\photos\a.arw")]
    public void DoesNotTreatOtherFormatsAsRaw(string path)
    {
        Assert.False(ThumbnailGenerator.IsRawFile(path));
    }

    [Theory]
    [InlineData(@"C:\photos\a.mp4")]
    [InlineData(@"C:\photos\a.MOV")]
    [InlineData(@"C:\photos\a.m4v")]
    // FFmpeg does the decoding, so Matroska thumbnails whether or not Media
    // Foundation can play it back.
    [InlineData(@"C:\photos\a.mkv")]
    [InlineData(@"C:\photos\a.MKV")]
    public void RecognisesVideos(string path)
    {
        Assert.True(ThumbnailGenerator.IsVideoFile(path));
        Assert.False(ThumbnailGenerator.IsImageFile(path));
    }

    [Theory]
    [InlineData(@"C:\photos\notes.txt")]
    [InlineData(@"C:\photos\clip.avi")]
    [InlineData(@"C:\photos\clip.webm")]
    [InlineData(@"C:\photos\noextension")]
    public void IgnoresEverythingElse(string path)
    {
        Assert.False(ThumbnailGenerator.IsImageFile(path));
        Assert.False(ThumbnailGenerator.IsVideoFile(path));
    }

    /// <summary>
    /// The published list and the predicates used to be maintained separately, and had
    /// already drifted. Anything the grid advertises must actually be recognised.
    /// </summary>
    [Fact]
    public void EveryAdvertisedExtensionIsRecognised()
    {
        foreach (var extension in ThumbnailGenerator.SupportedExtensions)
        {
            var path = @"C:\photos\a" + extension;
            Assert.True(
                ThumbnailGenerator.IsImageFile(path) || ThumbnailGenerator.IsVideoFile(path),
                $"{extension} is advertised as supported but is neither an image nor a video");
        }
    }

    [Fact]
    public void PhotoItemFlagsRawFiles()
    {
        var raw = new PhotoItem { FilePath = @"C:\photos\a.dng", FileName = "a.dng" };
        var jpeg = new PhotoItem { FilePath = @"C:\photos\a.jpg", FileName = "a.jpg" };
        var folder = new PhotoItem { FilePath = @"C:\photos\sub", FileName = "sub", IsFolder = true };

        Assert.True(raw.IsRaw);
        Assert.False(raw.IsVideo);
        Assert.False(jpeg.IsRaw);
        Assert.False(folder.IsRaw);
    }
}
