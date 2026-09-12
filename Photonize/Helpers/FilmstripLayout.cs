using System.Globalization;
using System.Text;

namespace Photonize.Helpers;

/// <summary>
/// Decides which frames the trim bar's filmstrip shows and builds the ffmpeg
/// filter graph that assembles them into one wide image.
///
/// Pure and ffmpeg-free, so <c>Photonize.Tests</c> can check the sample times and
/// the graph without ffmpeg installed -- the same split
/// <see cref="VideoFormatFingerprint"/> uses against <c>VideoEditService</c>.
/// </summary>
public static class FilmstripLayout
{
    /// <summary>
    /// Frames across the strip. Twelve reads as a filmstrip at any sensible
    /// dialog width without turning each cell into an unreadable sliver.
    /// </summary>
    public const int DefaultFrameCount = 12;

    /// <summary>
    /// Cell size. 12 x 160 gives a 1920px strip, which still looks sharp when a
    /// ~700px track is rendered at 150% DPI.
    /// </summary>
    public const int CellWidth = 160;

    public const int CellHeight = 90;

    /// <summary>
    /// When each frame is taken: the centre of the slice of the clip it stands
    /// for, not its leading edge. Sampling edges would put the first frame at
    /// t=0, which on most clips is a black fade-in, and would never show the tail.
    /// </summary>
    /// <remarks>
    /// The span is shrunk slightly rather than the tail being clipped. Clipping
    /// makes the last few samples of a very short clip land on the same instant,
    /// and hstack would then show the same frame several times over. Seeking past
    /// the final frame yields nothing at all, which fails the whole graph, so the
    /// margin cannot simply be dropped either.
    /// </remarks>
    public static IReadOnlyList<TimeSpan> SampleTimes(TimeSpan duration, int frameCount)
    {
        if (duration <= TimeSpan.Zero || frameCount <= 0)
            return Array.Empty<TimeSpan>();

        var seconds = duration.TotalSeconds;
        var usable = seconds - Math.Min(0.1, seconds * 0.05);

        var times = new List<TimeSpan>(frameCount);
        for (var i = 0; i < frameCount; i++)
        {
            times.Add(TimeSpan.FromSeconds(usable * (i + 0.5) / frameCount));
        }

        return times;
    }

    /// <summary>
    /// The -filter_complex graph: scale-and-crop every input to an identical cell,
    /// then lay them side by side.
    /// </summary>
    /// <remarks>
    /// force_original_aspect_ratio=increase followed by crop fills the cell and
    /// trims the overflow, so a portrait phone clip and 16:9 footage both produce
    /// a uniform strip. hstack requires every input to share a height, which this
    /// guarantees by construction.
    ///
    /// Every number is written with InvariantCulture, for the reason
    /// VideoEditService.BuildConcatFilter already documents: on a machine with a
    /// comma decimal separator the graph silently becomes nonsense.
    /// </remarks>
    public static string BuildFilterGraph(int frameCount, int cellWidth = CellWidth, int cellHeight = CellHeight)
    {
        if (frameCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(frameCount), "A filmstrip needs at least one frame.");

        var graph = new StringBuilder();

        for (var i = 0; i < frameCount; i++)
        {
            graph.AppendFormat(
                CultureInfo.InvariantCulture,
                "[{0}:v]scale={1}:{2}:force_original_aspect_ratio=increase,crop={1}:{2},setsar=1[c{0}];",
                i,
                cellWidth,
                cellHeight);
        }

        for (var i = 0; i < frameCount; i++)
        {
            graph.AppendFormat(CultureInfo.InvariantCulture, "[c{0}]", i);
        }

        graph.AppendFormat(CultureInfo.InvariantCulture, "hstack=inputs={0}[strip]", frameCount);

        return graph.ToString();
    }

    /// <summary>
    /// Which cell of a finished strip covers <paramref name="time"/>. Used to crop
    /// a still out of the strip when Windows has no codec to preview the clip.
    /// </summary>
    public static int CellIndexAt(TimeSpan time, TimeSpan duration, int frameCount)
    {
        if (duration <= TimeSpan.Zero || frameCount <= 0)
            return 0;

        var fraction = Math.Clamp(time.TotalSeconds / duration.TotalSeconds, 0, 1);
        return Math.Clamp((int)(fraction * frameCount), 0, frameCount - 1);
    }
}
