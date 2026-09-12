namespace Photonize.Helpers;

/// <summary>
/// Maps between a position on the trim bar and a time in the clip, and keeps the
/// two handles the right way round.
///
/// Pure, and free of <c>System.Windows</c> types, for the same reason
/// <see cref="VideoOutputNaming"/> and <see cref="DragPayloadBuilder"/> are:
/// <c>Photonize.Tests</c> can then drive the geometry without a WPF visual tree,
/// which is the only practical way to test a drag.
/// </summary>
public static class TrimGeometry
{
    /// <summary>
    /// Shortest range the handles will allow. This is what stops a drag inverting
    /// the selection -- the dragged handle stops here rather than passing its
    /// partner, which is how the iPhone's trimmer behaves.
    ///
    /// Deliberately the same value the extract itself validates against: if the
    /// handles could produce a range the service then rejects, the Extract button
    /// would sit enabled over a range that cannot be written.
    /// </summary>
    public static TimeSpan MinimumRange => VideoOutputNaming.MinimumClipLength;

    /// <summary>
    /// Where on the track a time sits. Returns 0 for a clip of unknown length or
    /// a track that has not been laid out yet, so a control positioned before its
    /// first layout pass cannot divide by zero.
    /// </summary>
    public static double TimeToX(TimeSpan time, TimeSpan duration, double trackWidth)
    {
        if (duration <= TimeSpan.Zero || trackWidth <= 0)
            return 0;

        var fraction = Math.Clamp(time.TotalSeconds / duration.TotalSeconds, 0, 1);
        return fraction * trackWidth;
    }

    /// <summary>The time under a position on the track, clamped to the clip.</summary>
    public static TimeSpan XToTime(double x, TimeSpan duration, double trackWidth)
    {
        if (duration <= TimeSpan.Zero || trackWidth <= 0)
            return TimeSpan.Zero;

        var fraction = Math.Clamp(x / trackWidth, 0, 1);
        return TimeSpan.FromSeconds(fraction * duration.TotalSeconds);
    }

    /// <summary>
    /// Moves the in-point to <paramref name="wanted"/>, holding the out-point
    /// still unless the clip is too short to keep them <see cref="MinimumRange"/>
    /// apart, in which case the out-point gives ground.
    ///
    /// Clamping the result rather than the accumulated drag is deliberate: drag
    /// past the end and back again and the handle returns to where it was, which
    /// is how every other slider in the app behaves.
    /// </summary>
    public static (TimeSpan Start, TimeSpan End) ClampStart(TimeSpan wanted, TimeSpan end, TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
            return (TimeSpan.Zero, TimeSpan.Zero);

        var start = Clamp(wanted, TimeSpan.Zero, duration);
        var outPoint = Clamp(end, TimeSpan.Zero, duration);

        if (start > outPoint - MinimumRange)
            start = outPoint - MinimumRange;

        if (start < TimeSpan.Zero)
        {
            // The out-point was within MinimumRange of zero, so it has to move.
            start = TimeSpan.Zero;
            outPoint = Min(duration, MinimumRange);
        }

        return (start, outPoint);
    }

    /// <summary>
    /// Moves the out-point to <paramref name="wanted"/>, holding the in-point
    /// still unless doing so would leave less than <see cref="MinimumRange"/>.
    /// </summary>
    public static (TimeSpan Start, TimeSpan End) ClampEnd(TimeSpan start, TimeSpan wanted, TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
            return (TimeSpan.Zero, TimeSpan.Zero);

        var inPoint = Clamp(start, TimeSpan.Zero, duration);
        var end = Clamp(wanted, TimeSpan.Zero, duration);

        if (end < inPoint + MinimumRange)
            end = inPoint + MinimumRange;

        if (end > duration)
        {
            // The in-point was within MinimumRange of the end, so it has to move.
            end = duration;
            inPoint = Max(TimeSpan.Zero, duration - MinimumRange);
        }

        return (inPoint, end);
    }

    private static TimeSpan Clamp(TimeSpan value, TimeSpan min, TimeSpan max) =>
        value < min ? min : value > max ? max : value;

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;
}
