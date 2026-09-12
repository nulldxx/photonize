using System.Globalization;
using System.IO;

namespace Photonize.Helpers;

/// <summary>
/// Works out where slice and splice output goes. Pure: it never touches the
/// filesystem itself, taking an <c>exists</c> predicate instead, so
/// <c>Photonize.Tests</c> can drive the collision rules without writing files.
/// Kept free of <c>System.Windows</c> types for the same reason, matching
/// <see cref="DragPayloadBuilder"/> and <see cref="ListReorder"/>.
/// </summary>
public static class VideoOutputNaming
{
    /// <summary>
    /// ffmpeg's segment muxer expands this to -part-001, -part-002 ... (with
    /// -segment_start_number 1). The whole family has to be reserved up front,
    /// because ffmpeg has no "don't clobber" mode for segment output: it would
    /// happily overwrite an earlier run's parts one by one.
    /// </summary>
    public const string PartInfix = "-part-";

    private const string SegmentNumberPattern = "%03d";

    /// <summary>
    /// The output pattern handed to ffmpeg, e.g. <c>C:\v\clip-part-%03d.mp4</c>.
    /// Parts keep the <b>source's</b> extension: a .mov holding ProRes or HEVC
    /// will not stream-copy into an .mp4 container, and slicing is copy-only.
    /// </summary>
    /// <param name="sourcePath">The clip being sliced.</param>
    /// <param name="exists">Existence probe, normally <see cref="File.Exists"/>.</param>
    public static string SlicePattern(string sourcePath, Func<string, bool> exists)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(exists);

        var directory = Path.GetDirectoryName(sourcePath) ?? string.Empty;
        var stem = Path.GetFileNameWithoutExtension(sourcePath);
        var extension = Path.GetExtension(sourcePath);

        // Suffix the *name*, not the part number: "clip-2-part-001.mov" reads as
        // a second run of clip, where "clip-part-2-001.mov" reads as nothing.
        var candidate = stem;
        for (var attempt = 2; FirstPartExists(directory, candidate, extension, exists); attempt++)
        {
            candidate = $"{stem}-{attempt}";
        }

        return Path.Combine(directory, $"{candidate}{PartInfix}{SegmentNumberPattern}{extension}");
    }

    /// <summary>
    /// The glob matching every part a <see cref="SlicePattern"/> run will write.
    /// Used to count progress and to clean up after a cancelled slice.
    /// </summary>
    public static string PartSearchPattern(string slicePattern)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slicePattern);

        var fileName = Path.GetFileName(slicePattern);
        return fileName.Replace(SegmentNumberPattern, "*", StringComparison.Ordinal);
    }

    private static bool FirstPartExists(string directory, string stem, string extension, Func<string, bool> exists) =>
        exists(Path.Combine(directory, $"{stem}{PartInfix}001{extension}"));

    /// <summary>
    /// Where a splice lands: the first clip's name plus "-spliced", in the same
    /// folder. Grid order decides which clip is first, so the name matches what
    /// the user saw at the front of the selection.
    /// </summary>
    /// <param name="sourcePaths">Inputs, in grid order.</param>
    /// <param name="reencoded">
    /// True when the clips had to be normalised. A re-encode always emits
    /// h264/aac, which only .mp4 reliably holds.
    /// </param>
    public static string SpliceOutputPath(
        IReadOnlyList<string> sourcePaths,
        bool reencoded,
        Func<string, bool> exists)
    {
        ArgumentNullException.ThrowIfNull(sourcePaths);
        ArgumentNullException.ThrowIfNull(exists);

        if (sourcePaths.Count == 0)
            throw new ArgumentException("At least one source is required.", nameof(sourcePaths));

        var first = sourcePaths[0];
        var directory = Path.GetDirectoryName(first) ?? string.Empty;
        var stem = Path.GetFileNameWithoutExtension(first);
        var extension = SpliceExtension(sourcePaths, reencoded);

        var candidate = Path.Combine(directory, $"{stem}-spliced{extension}");
        for (var attempt = 2; exists(candidate); attempt++)
        {
            candidate = Path.Combine(directory, $"{stem}-spliced-{attempt}{extension}");
        }

        return candidate;
    }

    /// <summary>
    /// Container for a splice: the shared extension when every input agrees and
    /// the streams are being copied, otherwise .mp4.
    /// </summary>
    public static string SpliceExtension(IReadOnlyList<string> sourcePaths, bool reencoded)
    {
        ArgumentNullException.ThrowIfNull(sourcePaths);

        if (reencoded || sourcePaths.Count == 0)
            return ".mp4";

        var first = Path.GetExtension(sourcePaths[0]);
        var allAgree = sourcePaths.All(path =>
            string.Equals(Path.GetExtension(path), first, StringComparison.OrdinalIgnoreCase));

        return allAgree ? first.ToLowerInvariant() : ".mp4";
    }

    /// <summary>
    /// ffmpeg's muxer name for a container, stated outright because the extension
    /// is not reliably inferred — .m4v is not, and the segment muxer needs telling
    /// either way. Anything unrecognised falls back to mp4, which is what Photonize
    /// writes when it has a free choice.
    /// </summary>
    public static string MuxerFor(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".mov" => "mov",
            ".mkv" => "matroska",
            _ => "mp4"
        };
    }

    /// <summary>
    /// Whether -movflags +faststart applies to this output. It is an option of
    /// ffmpeg's mov/mp4 muxer, so passing it to matroska does not merely go
    /// ignored — ffmpeg fails the whole run with "Option movflags not found".
    /// </summary>
    public static bool SupportsFaststart(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return MuxerFor(path) is "mov" or "mp4";
    }

    /// <summary>
    /// How many parts a slice will produce. The last part is short unless the
    /// duration divides exactly.
    /// </summary>
    public static int ExpectedPartCount(TimeSpan duration, int partLengthSeconds)
    {
        if (partLengthSeconds <= 0 || duration <= TimeSpan.Zero)
            return 0;

        return (int)Math.Ceiling(duration.TotalSeconds / partLengthSeconds);
    }

    /// <summary>
    /// Validates a part length against a clip. A length at or beyond the clip's
    /// duration would just copy the file, which is never what the user meant.
    /// </summary>
    public static bool IsValidPartLength(int seconds, TimeSpan? clipDuration, out string error)
    {
        if (seconds <= 0)
        {
            error = "Enter a part length of at least 1 second.";
            return false;
        }

        if (clipDuration is { } duration && duration > TimeSpan.Zero && seconds >= (int)duration.TotalSeconds)
        {
            error = $"That is longer than the clip ({FormatDuration(duration)}), so there would be nothing to cut.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    /// <summary>
    /// Marks a trimmed extract, the way <see cref="PartInfix"/> marks a slice.
    /// </summary>
    public const string ClipSuffix = "-clip";

    /// <summary>
    /// Shortest range Extract clip will write. Anything under this is almost
    /// certainly a slipped handle rather than an intended clip.
    /// </summary>
    public static readonly TimeSpan MinimumClipLength = TimeSpan.FromSeconds(0.5);

    /// <summary>
    /// Float seconds do not survive a round trip through ffprobe exactly, so a
    /// range that reaches the end of the clip can read as a few ticks past it.
    /// </summary>
    private static readonly TimeSpan Tolerance = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// Where an extracted clip lands: the source's name plus "-clip", in the same
    /// folder, stepping to -clip-2 rather than overwriting an earlier extract.
    /// </summary>
    public static string ClipOutputPath(string sourcePath, bool reencoded, Func<string, bool> exists)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(exists);

        var directory = Path.GetDirectoryName(sourcePath) ?? string.Empty;
        var stem = Path.GetFileNameWithoutExtension(sourcePath);
        var extension = ClipExtension(sourcePath, reencoded);

        var candidate = Path.Combine(directory, $"{stem}{ClipSuffix}{extension}");
        for (var attempt = 2; exists(candidate); attempt++)
        {
            candidate = Path.Combine(directory, $"{stem}{ClipSuffix}-{attempt}{extension}");
        }

        return candidate;
    }

    /// <summary>
    /// Container for an extract. A fast cut copies the streams, so it has to keep
    /// the source container -- ProRes in a .mov will not stream-copy into .mp4,
    /// the same constraint <see cref="SlicePattern"/> works under. A precise cut
    /// re-encodes to h264/aac, which only .mp4 reliably holds.
    /// </summary>
    public static string ClipExtension(string sourcePath, bool reencoded)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        return reencoded ? ".mp4" : Path.GetExtension(sourcePath).ToLowerInvariant();
    }

    /// <summary>
    /// Validates a trim range against the clip it came from, phrased for the
    /// dialog's read-out.
    /// </summary>
    public static bool IsValidTrimRange(TimeSpan start, TimeSpan end, TimeSpan duration, out string error)
    {
        if (duration <= TimeSpan.Zero)
        {
            error = "Photonize could not read this clip's length.";
            return false;
        }

        if (start < TimeSpan.Zero)
        {
            error = "The start is before the beginning of the clip.";
            return false;
        }

        if (end > duration + Tolerance)
        {
            error = "The end is past the end of the clip.";
            return false;
        }

        if (end - start < MinimumClipLength)
        {
            error = $"Select at least {MinimumClipLength.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture)} seconds.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    /// <summary>
    /// mm:ss.t for the trim read-out. <see cref="FormatDuration"/> carries whole
    /// seconds, which is too coarse to position a handle by.
    /// </summary>
    public static string FormatTimecode(TimeSpan time)
    {
        var tenths = time.Milliseconds / 100;

        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours}:{time.Minutes:D2}:{time.Seconds:D2}.{tenths}"
            : $"{time.Minutes:D2}:{time.Seconds:D2}.{tenths}";
    }

    /// <summary>
    /// "00:04.0 -> 00:11.5 (7.5s)". Invariant throughout: a comma decimal
    /// separator would make the read-out disagree with the tests on a German
    /// machine, and the string is also used in completion messages.
    /// </summary>
    public static string FormatTrimRange(TimeSpan start, TimeSpan end)
    {
        var length = (end - start).TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture);

        return $"{FormatTimecode(start)} \u2192 {FormatTimecode(end)} ({length}s)";
    }

    /// <summary>Human-readable clip length for dialogs and status messages.</summary>
    public static string FormatDuration(TimeSpan duration) =>
        duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}:{duration.Minutes:D2}:{duration.Seconds:D2}"
            : $"{duration.Minutes}:{duration.Seconds:D2}";
}
