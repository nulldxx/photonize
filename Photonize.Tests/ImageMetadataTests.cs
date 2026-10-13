using System.IO;
using System.Text;
using Photonize.Models;
using Photonize.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.Metadata.Profiles.Xmp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Photonize.Tests;

/// <summary>
/// Exported and upscaled images must keep the source's EXIF (capture date, GPS) and XMP.
/// </summary>
public class ImageMetadataTests : IDisposable
{
    private const string DateTaken = "2024:06:01 12:34:56";
    private const string XmpMarker = "photonize-test-marker";

    private readonly string _directory;

    public ImageMetadataTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"PhotonizeMetadata_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch { }
    }

    [Theory]
    [InlineData(ImageFormat.WebP, ".webp")]
    [InlineData(ImageFormat.PNG, ".png")]
    [InlineData(ImageFormat.JPG, ".jpg")]
    public async Task ExportKeepsExifAndXmp(ImageFormat format, string extension)
    {
        var source = WriteSourceJpeg("photo.jpg");

        var (success, message) = await new ImageExporter().ExportToFormatAsync(
            new List<PhotoItem> { new() { FilePath = source, FileName = "photo.jpg" } },
            _directory,
            format);

        Assert.True(success, message);
        var output = Path.Combine(_directory, format.ToString(), "photo" + extension);
        AssertCarriesSourceMetadata(Image.Identify(output).Metadata);
    }

    [Fact]
    public async Task WebPExporterKeepsExifAndXmp()
    {
        var source = WriteSourceJpeg("photo.jpg");

        var (success, message) = await new WebPExporter().ExportToWebPAsync(
            new List<PhotoItem> { new() { FilePath = source, FileName = "photo.jpg" } },
            _directory);

        Assert.True(success, message);
        AssertCarriesSourceMetadata(Image.Identify(Path.Combine(_directory, "WebP", "photo.webp")).Metadata);
    }

    [Fact]
    public void CopierReplacesMetadataAndUpdatesDimensions()
    {
        var source = Image.Identify(WriteSourceJpeg("photo.jpg")).Metadata;

        // Stands in for a Topaz intermediate: bigger, with metadata of its own.
        using var upscaled = new Image<Rgba32>(256, 128);
        upscaled.Metadata.ExifProfile = new ExifProfile();
        upscaled.Metadata.ExifProfile.SetValue(ExifTag.DateTimeOriginal, "1999:01:01 00:00:00");

        ImageMetadataCopier.CopyFrom(source, upscaled);

        AssertCarriesSourceMetadata(upscaled.Metadata);
        var exif = upscaled.Metadata.ExifProfile!;
        Assert.True(exif.TryGetValue(ExifTag.PixelXDimension, out var width));
        Assert.True(exif.TryGetValue(ExifTag.PixelYDimension, out var height));
        Assert.Equal(256u, (uint)width!.Value);
        Assert.Equal(128u, (uint)height!.Value);
    }

    [Fact]
    public void CopierLeavesTargetAloneWhenSourceHasNoMetadata()
    {
        using var bare = new Image<Rgba32>(8, 8);
        using var upscaled = new Image<Rgba32>(16, 16);
        upscaled.Metadata.ExifProfile = new ExifProfile();
        upscaled.Metadata.ExifProfile.SetValue(ExifTag.DateTimeOriginal, DateTaken);

        ImageMetadataCopier.CopyFrom(bare.Metadata, upscaled);

        Assert.True(upscaled.Metadata.ExifProfile.TryGetValue(ExifTag.DateTimeOriginal, out var date));
        Assert.Equal(DateTaken, date!.Value);
    }

    private string WriteSourceJpeg(string fileName)
    {
        var path = Path.Combine(_directory, fileName);
        using var image = new Image<Rgba32>(64, 32);

        var exif = new ExifProfile();
        exif.SetValue(ExifTag.DateTimeOriginal, DateTaken);
        exif.SetValue(ExifTag.GPSLatitudeRef, "N");
        exif.SetValue(ExifTag.GPSLatitude, new[] { new Rational(51, 1), new Rational(30, 1), new Rational(0, 1) });
        image.Metadata.ExifProfile = exif;

        var xmp = $"""
            <x:xmpmeta xmlns:x="adobe:ns:meta/">
              <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
                <rdf:Description xmlns:dc="http://purl.org/dc/elements/1.1/">
                  <dc:source>{XmpMarker}</dc:source>
                </rdf:Description>
              </rdf:RDF>
            </x:xmpmeta>
            """;
        image.Metadata.XmpProfile = new XmpProfile(Encoding.UTF8.GetBytes(xmp));

        image.SaveAsJpeg(path);
        return path;
    }

    private static void AssertCarriesSourceMetadata(ImageMetadata metadata)
    {
        var exif = metadata.ExifProfile;
        Assert.NotNull(exif);
        Assert.True(exif.TryGetValue(ExifTag.DateTimeOriginal, out var date));
        Assert.Equal(DateTaken, date!.Value);
        Assert.True(exif.TryGetValue(ExifTag.GPSLatitudeRef, out var latRef));
        Assert.Equal("N", latRef!.Value);
        Assert.True(exif.TryGetValue(ExifTag.GPSLatitude, out var lat));
        Assert.Equal(51u, lat!.Value![0].Numerator);

        Assert.NotNull(metadata.XmpProfile);
        Assert.Contains(XmpMarker, metadata.XmpProfile.GetDocument()!.ToString());
    }
}
