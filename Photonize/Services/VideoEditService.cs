using System.Globalization;
using System.IO;
using System.Text;
using FFMpegCore;
using Photonize.Helpers;

namespace Photonize.Services;

/// <summary>
/// Slices one video into consecutive parts, and splices several into one.
///
/// Both go through FFMpegCore's fluent API rather than a hand-rolled
/// <c>Process</c>: it quotes output paths, and its <c>FromDemuxConcatInput</c>
/// already writes, escapes ("'" becomes "'\''") and cleans up the concat list
/// file, which is the fiddly half of a demuxer concat.
///
/// Shaped like <see cref="TopazPhotoAIService"/> — a (Success, Message) tuple,
/// an optional progress reporter and a cancellation token — so
/// <c>MainViewModel</c> can drive it with the existing ProgressDialog pattern.
/// </summary>
public class VideoEditService
{
    /// <summary>
    /// Moves the moov atom to the front so the result starts playing before it
    /// has fully downloaded. .mov and .mp4 share ffmpeg's mov muxer, so this is
    /// valid for both containers Photonize writes.
    /// </summary>
    private const string Faststart = "-movflags +faststart";

    public bool IsAvailable => FFmpegLocator.IsAvailable;

    /// <summary>Clip length, or null when ffprobe cannot read the file.</summary>
    public async Task<TimeSpan?> GetDurationAsync(string filePath)
    {
        try
        {
            var analysis = await FFProbe.AnalyseAsync(filePath);
            return analysis.Duration > TimeSpan.Zero ? analysis.Duration : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Cuts each clip into consecutive parts of roughly
    /// <paramref name="partLengthSeconds"/>.
    ///
    /// Stream copy, so it runs at disk speed and loses nothing — but cuts can
    /// only land on keyframes, so parts vary by a second or two either way.
    /// </summary>
    public async Task<(bool Success, string Message)> SliceAsync(
        IReadOnlyList<string> videoPaths,
        int partLengthSeconds,
        IProgress<(int current, int total, string status)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (videoPaths == null || videoPaths.Count == 0)
            return (false, "No videos to slice.");

        if (partLengthSeconds <= 0)
            return (false, "Part length must be at least 1 second.");

        if (!IsAvailable)
            return (false, "FFmpeg was not found. Install it and restart Photonize.");

        var written = new List<string>();
        var failures = new List<string>();
        var clipsDone = 0;

        foreach (var sourcePath in videoPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var name = Path.GetFileName(sourcePath);
            var pattern = VideoOutputNaming.SlicePattern(sourcePath, File.Exists);

            try
            {
                var duration = await GetDurationAsync(sourcePath);
                var expected = duration is { } clipLength
                    ? VideoOutputNaming.ExpectedPartCount(clipLength, partLengthSeconds)
                    : 0;

                var parts = await SliceOneAsync(
                    sourcePath, pattern, partLengthSeconds, expected,
                    clipsDone, videoPaths.Count, name, progress, cancellationToken);

                written.AddRange(parts);
            }
            catch (OperationCanceledException)
            {
                // Segments already on disk are truncated at the kill point, and a
                // half-written clip in the grid is worse than none. Clean up the
                // family this run reserved -- and only that family, which is why
                // SlicePattern picks an unused one before ffmpeg starts.
                DeleteParts(pattern);
                throw;
            }
            catch (Exception ex)
            {
                DeleteParts(pattern);
                failures.Add($"{name}: {Summarise(ex)}");
            }

            clipsDone++;
        }

        return BuildSliceMessage(written, failures, videoPaths.Count);
    }

    private async Task<List<string>> SliceOneAsync(
        string sourcePath,
        string pattern,
        int partLengthSeconds,
        int expectedParts,
        int clipsDone,
        int clipCount,
        string name,
        IProgress<(int current, int total, string status)>? progress,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(pattern) ?? string.Empty;
        var searchPattern = VideoOutputNaming.PartSearchPattern(pattern);

        // Progress by counting the parts as they land, not by ffmpeg's reported
        // timestamp: -reset_timestamps restarts each part's clock at zero, so the
        // reported time sawtooths between 0 and the part length instead of
        // advancing through the clip.
        using var progressCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var watcher = WatchPartsAsync(
            directory, searchPattern, expectedParts, clipsDone, clipCount, name, progress, progressCts.Token);

        try
        {
            await FFMpegArguments
                .FromFileInput(sourcePath)
                .OutputToFile(pattern, overwrite: true, options => options
                    // First video stream plus audio if there is any. Plain "-map 0"
                    // would drag in the tmcd timecode track iPhone .mov files carry,
                    // which the segment muxer refuses.
                    .WithCustomArgument("-map 0:v:0 -map 0:a?")
                    .CopyChannel()
                    .ForceFormat("segment")
                    .WithCustomArgument(string.Format(
                        CultureInfo.InvariantCulture,
                        "-segment_format {0} -segment_time {1} -segment_start_number 1 " +
                        "-reset_timestamps 1 -avoid_negative_ts make_zero",
                        SegmentFormatFor(sourcePath),
                        partLengthSeconds)))
                .CancellableThrough(cancellationToken)
                .ProcessAsynchronously();
        }
        finally
        {
            progressCts.Cancel();
            try { await watcher; } catch (OperationCanceledException) { }
        }

        var parts = Directory.GetFiles(directory, searchPattern).OrderBy(p => p, StringComparer.Ordinal).ToList();
        progress?.Report((
            (clipsDone + 1) * 100 / Math.Max(1, clipCount),
            100,
            $"{name}: {parts.Count} part(s)"));

        return parts;
    }

    private static async Task WatchPartsAsync(
        string directory,
        string searchPattern,
        int expectedParts,
        int clipsDone,
        int clipCount,
        string name,
        IProgress<(int current, int total, string status)>? progress,
        CancellationToken cancellationToken)
    {
        if (progress == null)
            return;

        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));

        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            int found;
            try
            {
                found = Directory.GetFiles(directory, searchPattern).Length;
            }
            catch (DirectoryNotFoundException)
            {
                return;
            }

            var withinClip = expectedParts > 0
                ? Math.Min(100, found * 100 / expectedParts)
                : 0;
            var overall = (clipsDone * 100 + withinClip) / Math.Max(1, clipCount);

            progress.Report((
                overall,
                100,
                expectedParts > 0
                    ? $"Slicing {name} — part {found} of about {expectedParts}"
                    : $"Slicing {name} — {found} part(s) so far"));
        }
    }

    /// <summary>
    /// .m4v is not reliably inferred from the extension, so the segment muxer's
    /// container is always stated outright.
    /// </summary>
    private static string SegmentFormatFor(string path) =>
        string.Equals(Path.GetExtension(path), ".mov", StringComparison.OrdinalIgnoreCase) ? "mov" : "mp4";

    private static void DeleteParts(string pattern)
    {
        try
        {
            var directory = Path.GetDirectoryName(pattern) ?? string.Empty;
            foreach (var file in Directory.GetFiles(directory, VideoOutputNaming.PartSearchPattern(pattern)))
            {
                try { File.Delete(file); } catch { /* best effort */ }
            }
        }
        catch
        {
            // Nothing useful to do if the folder has gone.
        }
    }

    private static (bool, string) BuildSliceMessage(List<string> written, List<string> failures, int clipCount)
    {
        if (written.Count == 0)
        {
            return (false, failures.Count > 0
                ? $"Slice failed:\n{string.Join("\n", failures)}"
                : "Slice produced no output.");
        }

        var message = clipCount == 1
            ? $"Sliced into {written.Count} part(s)."
            : $"Sliced {clipCount} clip(s) into {written.Count} part(s).";

        if (failures.Count > 0)
            message += $"\n\n{failures.Count} failed:\n{string.Join("\n", failures)}";

        return (true, message);
    }

    /// <summary>
    /// Joins several clips into one, in the order given (grid order).
    ///
    /// Probes them first: clips that already agree are concatenated by copying
    /// their streams, which is instant and lossless. Anything else is normalised
    /// to the first clip and re-encoded, which is slow but always works.
    /// </summary>
    public async Task<(bool Success, string Message)> SpliceAsync(
        IReadOnlyList<string> videoPaths,
        IProgress<(int current, int total, string status)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (videoPaths == null || videoPaths.Count < 2)
            return (false, "Select at least two videos to splice.");

        if (!IsAvailable)
            return (false, "FFmpeg was not found. Install it and restart Photonize.");

        progress?.Report((0, 100, "Checking clips\u2026"));

        List<VideoFormatFingerprint> fingerprints;
        try
        {
            fingerprints = await FingerprintAsync(videoPaths, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return (false, $"Could not read the clips: {Summarise(ex)}");
        }

        var unreadable = fingerprints.FirstOrDefault(f => f.Width == 0 || f.Height == 0);
        if (unreadable != null)
            return (false, $"{unreadable.FileName} has no video track, so it cannot be spliced.");

        var mismatch = VideoConcatCompatibility.DescribeMismatch(fingerprints);
        var reencode = mismatch != null;
        var outputPath = VideoOutputNaming.SpliceOutputPath(videoPaths, reencode, File.Exists);

        progress?.Report((0, 100, reencode
            ? $"Re-encoding \u2014 {mismatch}"
            : "Joining clips without re-encoding\u2026"));

        try
        {
            var totalSeconds = fingerprints.Sum(f => f.DurationSeconds);
            var reporter = ProgressReporter(progress, totalSeconds, videoPaths.Count, reencode);

            if (reencode)
            {
                await RunReencodeConcatAsync(videoPaths, fingerprints, outputPath, reporter, cancellationToken);
            }
            else
            {
                await RunCopyConcatAsync(
                    videoPaths,
                    TimeSpan.FromSeconds(Math.Max(1, totalSeconds)),
                    outputPath,
                    reporter,
                    cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            TryDelete(outputPath);
            throw;
        }
        catch (Exception ex)
        {
            TryDelete(outputPath);
            return (false, $"Splice failed: {Summarise(ex)}");
        }

        if (!File.Exists(outputPath))
            return (false, "Splice produced no output.");

        var name = Path.GetFileName(outputPath);
        return (true, reencode
            ? $"Spliced {videoPaths.Count} clips into '{name}'.\n\nThe clips had to be re-encoded because {mismatch}."
            : $"Spliced {videoPaths.Count} clips into '{name}' without re-encoding.");
    }

    private static Action<double> ProgressReporter(
        IProgress<(int current, int total, string status)>? progress,
        double totalSeconds,
        int clipCount,
        bool reencode)
    {
        var verb = reencode ? "Re-encoding" : "Joining";

        return percent =>
        {
            var clamped = (int)Math.Clamp(percent, 0, 100);
            var elapsed = TimeSpan.FromSeconds(totalSeconds * clamped / 100.0);
            progress?.Report((
                clamped,
                100,
                $"{verb} {clipCount} clips \u2014 {VideoOutputNaming.FormatDuration(elapsed)} of " +
                $"{VideoOutputNaming.FormatDuration(TimeSpan.FromSeconds(totalSeconds))}"));
        };
    }

    /// <summary>
    /// The lossless path. FromDemuxConcatInput builds the concat list file,
    /// escapes apostrophes in the paths and deletes the list afterwards.
    /// </summary>
    private static async Task RunCopyConcatAsync(
        IReadOnlyList<string> videoPaths,
        TimeSpan totalDuration,
        string outputPath,
        Action<double> onProgress,
        CancellationToken cancellationToken)
    {
        await FFMpegArguments
            .FromDemuxConcatInput(videoPaths)
            .OutputToFile(outputPath, overwrite: true, options => options
                // +genpts repairs the timestamp discontinuity at each join; without
                // it players stall at the seam between two copied clips.
                .WithCustomArgument("-fflags +genpts -map 0:v:0 -map 0:a?")
                .CopyChannel()
                .WithCustomArgument(Faststart))
            .NotifyOnProgress(onProgress, totalDuration)
            .CancellableThrough(cancellationToken)
            .ProcessAsynchronously();
    }

    /// <summary>
    /// The normalising path. Every clip is scaled and padded into the first
    /// clip's frame, and silent clips get generated silence so the audio track
    /// runs the whole length -- the failure a plain copy-concat hides.
    /// </summary>
    private static async Task RunReencodeConcatAsync(
        IReadOnlyList<string> videoPaths,
        IReadOnlyList<VideoFormatFingerprint> fingerprints,
        string outputPath,
        Action<double> onProgress,
        CancellationToken cancellationToken)
    {
        var first = fingerprints[0];
        var anyAudio = fingerprints.Any(f => f.HasAudio);

        var arguments = FFMpegArguments.FromFileInput(videoPaths[0]);
        for (var i = 1; i < videoPaths.Count; i++)
        {
            var path = videoPaths[i];
            arguments = arguments.AddFileInput(path);
        }

        // One generated silent track per silent clip, each exactly that clip's
        // length, appended after the real inputs so the indices stay predictable.
        var silentClips = new List<int>();
        if (anyAudio)
        {
            for (var i = 0; i < fingerprints.Count; i++)
            {
                if (fingerprints[i].HasAudio)
                    continue;

                silentClips.Add(i);

                // Copied out of the loop variable: WithInput invokes this action
                // immediately, but capturing "i" directly is a trap waiting for
                // the day it doesn't.
                var seconds = Math.Max(0.04, fingerprints[i].DurationSeconds);
                arguments = arguments.AddFileInput(
                    "anullsrc=channel_layout=stereo:sample_rate=48000",
                    verifyExists: false,
                    options => options.WithCustomArgument(string.Format(
                        CultureInfo.InvariantCulture,
                        "-f lavfi -t {0:0.###}",
                        seconds)));
            }
        }

        var graph = BuildConcatFilter(fingerprints, first, anyAudio, silentClips);
        var totalDuration = TimeSpan.FromSeconds(Math.Max(1, fingerprints.Sum(f => f.DurationSeconds)));

        await arguments
            .OutputToFile(outputPath, overwrite: true, options => options
                .WithCustomArgument($"-filter_complex \"{graph}\"")
                .WithCustomArgument(anyAudio ? "-map [vout] -map [aout]" : "-map [vout]")
                .WithCustomArgument("-c:v libx264 -preset medium -crf 18 -pix_fmt yuv420p")
                .WithCustomArgument(anyAudio ? "-c:a aac -b:a 192k -ar 48000 -ac 2" : "-an")
                .WithCustomArgument(Faststart))
            .NotifyOnProgress(onProgress, totalDuration)
            .CancellableThrough(cancellationToken)
            .ProcessAsynchronously();
    }

    /// <summary>
    /// Builds the -filter_complex graph. Every rate and ratio is written with
    /// InvariantCulture: on a machine with a comma decimal separator, "29,97"
    /// silently corrupts the graph.
    /// </summary>
    internal static string BuildConcatFilter(
        IReadOnlyList<VideoFormatFingerprint> fingerprints,
        VideoFormatFingerprint target,
        bool anyAudio,
        IReadOnlyList<int> silentClips)
    {
        var parts = new List<string>();
        var chain = new StringBuilder();

        for (var i = 0; i < fingerprints.Count; i++)
        {
            parts.Add(string.Format(
                CultureInfo.InvariantCulture,
                "[{0}:v:0]scale={1}:{2}:force_original_aspect_ratio=decrease," +
                "pad={1}:{2}:-1:-1:color=black,setsar=1,fps={3:0.####},format=yuv420p[v{0}]",
                i, target.Width, target.Height, target.ComparableFrameRate));
        }

        if (anyAudio)
        {
            // The generated silent tracks sit after all the real inputs, in the
            // order the silent clips appear, so clip N's silence is input
            // (count + its position in silentClips).
            var silenceInput = new Dictionary<int, int>();
            for (var slot = 0; slot < silentClips.Count; slot++)
            {
                silenceInput[silentClips[slot]] = fingerprints.Count + slot;
            }

            for (var i = 0; i < fingerprints.Count; i++)
            {
                var source = fingerprints[i].HasAudio ? i : silenceInput[i];
                parts.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "[{0}:a:0]aresample=48000,aformat=sample_fmts=fltp:channel_layouts=stereo,asetpts=N/SR/TB[a{1}]",
                    source, i));
            }
        }

        for (var i = 0; i < fingerprints.Count; i++)
        {
            chain.Append(CultureInfo.InvariantCulture, $"[v{i}]");
            if (anyAudio)
                chain.Append(CultureInfo.InvariantCulture, $"[a{i}]");
        }

        chain.Append(string.Format(
            CultureInfo.InvariantCulture,
            "concat=n={0}:v=1:a={1}[vout]{2}",
            fingerprints.Count,
            anyAudio ? 1 : 0,
            anyAudio ? "[aout]" : string.Empty));

        parts.Add(chain.ToString());
        return string.Join(";", parts);
    }

    private async Task<List<VideoFormatFingerprint>> FingerprintAsync(
        IReadOnlyList<string> videoPaths,
        CancellationToken cancellationToken)
    {
        var fingerprints = new List<VideoFormatFingerprint>(videoPaths.Count);

        foreach (var path in videoPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var analysis = await FFProbe.AnalyseAsync(path, cancellationToken: cancellationToken);
            var video = analysis.PrimaryVideoStream;
            var audio = analysis.PrimaryAudioStream;

            fingerprints.Add(new VideoFormatFingerprint(
                FileName: Path.GetFileName(path),
                VideoCodec: video?.CodecName ?? string.Empty,
                Width: video?.Width ?? 0,
                Height: video?.Height ?? 0,
                PixelFormat: video?.PixelFormat ?? string.Empty,
                FrameRate: video?.FrameRate ?? 0,
                Rotation: video?.Rotation ?? 0,
                HasAudio: audio != null,
                AudioCodec: audio?.CodecName ?? string.Empty,
                AudioSampleRateHz: audio?.SampleRateHz ?? 0,
                AudioChannels: audio?.Channels ?? 0)
            {
                DurationSeconds = analysis.Duration.TotalSeconds
            });
        }

        return fingerprints;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best effort.
        }
    }

    private static string Summarise(Exception ex)
    {
        // ffmpeg's stderr arrives as a wall of text; the last line carries the
        // actual reason and the rest is banner noise.
        var lines = ex.Message.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var last = lines.LastOrDefault()?.Trim();
        return string.IsNullOrWhiteSpace(last) ? ex.GetType().Name : last;
    }
}
