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

    /// <summary>Human-readable clip length for dialogs and status messages.</summary>
    public static string FormatDuration(TimeSpan duration) =>
        duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}:{duration.Minutes:D2}:{duration.Seconds:D2}"
            : $"{duration.Minutes}:{duration.Seconds:D2}";
}
