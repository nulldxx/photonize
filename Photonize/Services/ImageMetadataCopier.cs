using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;

namespace Photonize.Services;

/// <summary>
/// Carries a source photo's metadata onto a derived image whose own metadata cannot be
/// trusted — e.g. a Topaz upscale, which is re-encoded from whatever tpai chose to write.
/// </summary>
public static class ImageMetadataCopier
{
    /// <summary>
    /// Copies EXIF and XMP from <paramref name="source"/> onto <paramref name="target"/>,
    /// replacing the target's own. The target's ICC profile is kept when it has one,
    /// since the tool that produced it may have converted the colour; the source's is
    /// only used as a fallback. EXIF pixel dimensions are rewritten to the target's
    /// size so they don't describe the pre-upscale image.
    /// </summary>
    public static void CopyFrom(ImageMetadata source, Image target)
    {
        var metadata = target.Metadata;

        if (source.ExifProfile != null)
        {
            metadata.ExifProfile = source.ExifProfile.DeepClone();
        }

        if (source.XmpProfile != null)
        {
            metadata.XmpProfile = source.XmpProfile.DeepClone();
        }

        if (metadata.IccProfile == null && source.IccProfile != null)
        {
            metadata.IccProfile = source.IccProfile.DeepClone();
        }

        var exif = metadata.ExifProfile;
        if (exif != null)
        {
            exif.SetValue(ExifTag.PixelXDimension, (Number)(uint)target.Width);
            exif.SetValue(ExifTag.PixelYDimension, (Number)(uint)target.Height);
        }
    }
}
