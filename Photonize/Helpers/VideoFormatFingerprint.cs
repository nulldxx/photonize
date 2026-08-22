using System.Globalization;

namespace Photonize.Helpers;

/// <summary>
/// The stream shape of one clip, reduced to just the fields that decide whether
/// several clips can be joined by copying their streams or have to be re-encoded.
///
/// Pure and ffprobe-free by design: <c>VideoEditService</c> builds these from
/// <c>IMediaAnalysis</c>, and <c>Photonize.Tests</c> builds them by hand, so the
/// compatibility rules are testable without ffmpeg installed.
/// </summary>
public sealed record VideoFormatFingerprint(
    string FileName,
    string VideoCodec,
    int Width,
    int Height,
    string PixelFormat,
    double FrameRate,
    int Rotation,
    bool HasAudio,
    string AudioCodec,
    int AudioSampleRateHz,
    int AudioChannels)
{
    /// <summary>
    /// Clip length. Not part of the compatibility comparison -- clips of
    /// different lengths join perfectly well -- but needed to size the progress
    /// bar and to generate exactly enough silence for a silent clip.
    /// </summary>
    public double DurationSeconds { get; init; }

    /// <summary>
    /// Frame rates are compared at 2 dp so 29.97 and 30000/1001 — the same rate
    /// written two ways — count as equal, while 29.97 and 30 still differ.
    /// </summary>
    public double ComparableFrameRate => Math.Round(FrameRate, 2);
}

/// <summary>
/// Decides between the two splice strategies. Copying is near-instant and
/// lossless but demands that every clip already agree; anything else has to be
/// normalised to the first clip and re-encoded.
/// </summary>
public static class VideoConcatCompatibility
{
    /// <summary>
    /// True when every clip can be concatenated with <c>-c copy</c>.
    /// </summary>
    public static bool AreConcatCompatible(IReadOnlyList<VideoFormatFingerprint> clips) =>
        DescribeMismatch(clips) is null;

    /// <summary>
    /// The first reason the clips cannot be stream-copied, phrased for a status
    /// line, or null when they can. Naming the reason matters: "re-encoding
    /// because the frame rates differ" is actionable where a silent slow path is not.
    /// </summary>
    public static string? DescribeMismatch(IReadOnlyList<VideoFormatFingerprint> clips)
    {
        ArgumentNullException.ThrowIfNull(clips);

        if (clips.Count < 2)
            return null;

        var first = clips[0];

        // Mixed audio presence is the classic concat trap: -c copy produces a
        // file whose audio simply stops when the first silent clip begins, and
        // nothing warns you. Treat it as a mismatch so the re-encode path can
        // pad the silent clips with generated silence instead.
        if (clips.Any(clip => clip.HasAudio != first.HasAudio))
        {
            var silent = clips.First(clip => !clip.HasAudio);
            return $"{silent.FileName} has no audio track while the others do";
        }

        foreach (var clip in clips.Skip(1))
        {
            if (!string.Equals(clip.VideoCodec, first.VideoCodec, StringComparison.OrdinalIgnoreCase))
                return $"video codecs differ ({first.VideoCodec} vs {clip.VideoCodec})";

            if (clip.Width != first.Width || clip.Height != first.Height)
                return $"resolutions differ ({first.Width}x{first.Height} vs {clip.Width}x{clip.Height})";

            if (!string.Equals(clip.PixelFormat, first.PixelFormat, StringComparison.OrdinalIgnoreCase))
                return $"pixel formats differ ({first.PixelFormat} vs {clip.PixelFormat})";

            if (Math.Abs(clip.ComparableFrameRate - first.ComparableFrameRate) > double.Epsilon)
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "frame rates differ ({0:0.##} vs {1:0.##})",
                    first.ComparableFrameRate,
                    clip.ComparableFrameRate);

            // A portrait phone clip carries a rotation matrix rather than swapped
            // dimensions. Copy-concatenating it onto landscape footage rotates the
            // landscape footage too, because the container keeps one matrix.
            if (clip.Rotation != first.Rotation)
                return $"clip rotation differs ({first.Rotation}° vs {clip.Rotation}°)";

            if (!first.HasAudio)
                continue;

            if (!string.Equals(clip.AudioCodec, first.AudioCodec, StringComparison.OrdinalIgnoreCase))
                return $"audio codecs differ ({first.AudioCodec} vs {clip.AudioCodec})";

            if (clip.AudioSampleRateHz != first.AudioSampleRateHz)
                return $"audio sample rates differ ({first.AudioSampleRateHz} Hz vs {clip.AudioSampleRateHz} Hz)";

            if (clip.AudioChannels != first.AudioChannels)
                return $"audio channel counts differ ({first.AudioChannels} vs {clip.AudioChannels})";
        }

        // Container is deliberately NOT compared: .mp4 and .mov both hold
        // h264/aac and concat-copy into each other perfectly well.
        return null;
    }
}
