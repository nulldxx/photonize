using System.Globalization;
using System.IO;
using System.Windows.Media.Imaging;
using FFMpegCore;
using Photonize.Helpers;

namespace Photonize.Services;

/// <summary>
/// Builds the strip of frames behind the trim bar.
///
/// One ffmpeg run produces one wide image rather than a frame per file, for two
/// reasons. <c>FFMpeg.SnapshotAsync</c> probes the input before every snapshot,
/// so twelve frames would be twenty-four process launches; and FFMpegCore's
/// <c>OutputToFile</c> takes a single output, so several stills from one run is
/// not expressible anyway. Stacking them in the filter graph sidesteps both.
/// </summary>
public class FilmstripGenerator
{
    public bool IsAvailable => FFmpegLocator.IsAvailable;

    /// <summary>
    /// A single image holding <see cref="FilmstripLayout.DefaultFrameCount"/>
    /// frames side by side, frozen so it can cross back to the UI thread.
    /// Returns null when there is no ffmpeg or the clip cannot be read: a strip is
    /// a convenience, and the dialog stays usable without one.
    /// </summary>
    public async Task<BitmapSource?> GenerateAsync(
        string videoPath,
        TimeSpan duration,
        int frameCount,
        CancellationToken cancellationToken)
    {
        if (!IsAvailable || string.IsNullOrWhiteSpace(videoPath))
            return null;

        var times = FilmstripLayout.SampleTimes(duration, frameCount);
        if (times.Count == 0)
            return null;

        var tempFile = Path.Combine(Path.GetTempPath(), $"photonize_strip_{Guid.NewGuid():N}.jpg");

        try
        {
            // The same clip opened once per frame. Each -ss sits in front of its
            // own -i, which is an input seek: ffmpeg jumps to the nearest keyframe
            // rather than decoding forward from zero, so this costs the same on an
            // hour-long 4K clip as on a ten-second one. A -vf fps= filter would
            // decode the entire file instead, and take minutes over it.
            var arguments = FFMpegArguments.FromFileInput(
                videoPath, verifyExists: true, options => options.WithCustomArgument(SeekArgument(times[0])));

            for (var i = 1; i < times.Count; i++)
            {
                // Copied out of the loop variable: the options action runs
                // immediately, but capturing the iteration variable is a trap
                // waiting for the day it doesn't -- the same note
                // RunReencodeConcatAsync carries.
                var seekTo = times[i];
                arguments = arguments.AddFileInput(
                    videoPath, verifyExists: true, options => options.WithCustomArgument(SeekArgument(seekTo)));
            }

            var graph = FilmstripLayout.BuildFilterGraph(times.Count);

            await arguments
                .OutputToFile(tempFile, overwrite: true, options => options
                    .WithCustomArgument($"-filter_complex \"{graph}\"")
                    .WithCustomArgument("-map [strip]")
                    .WithFrameOutputCount(1)
                    // Photographic content, and it is thrown away as soon as it is
                    // decoded, so JPEG rather than PNG.
                    .WithCustomArgument("-q:v 4"))
                .CancellableThrough(cancellationToken)
                .ProcessAsynchronously();

            cancellationToken.ThrowIfCancellationRequested();

            if (!File.Exists(tempFile))
                return null;

            return LoadFrozen(tempFile);
        }
        finally
        {
            try
            {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }
            catch
            {
                // A stranded temp file is not worth failing the dialog over.
            }
        }
    }

    /// <summary>
    /// An input seek. Written out rather than taken from FFMpegCore, which has no
    /// -ss helper in this version -- it ships EndSeekArgument (-to) but no
    /// counterpart for -ss. InvariantCulture because a comma decimal separator
    /// makes ffmpeg read the timestamp as a different number entirely.
    /// </summary>
    private static string SeekArgument(TimeSpan time) =>
        string.Format(CultureInfo.InvariantCulture, "-ss {0:0.###}", time.TotalSeconds);

    /// <summary>
    /// Reads the whole file now and releases the handle, so the caller can delete
    /// it immediately, then freezes it for use off the UI thread -- the idiom
    /// <see cref="ThumbnailGenerator"/> already uses for video thumbnails.
    /// </summary>
    private static BitmapSource LoadFrozen(string path)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(path, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();

        return bitmap;
    }
}
