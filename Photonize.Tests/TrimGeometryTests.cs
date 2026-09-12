using Photonize.Helpers;
using Xunit;

namespace Photonize.Tests;

public class TrimGeometryTests
{
    private static readonly TimeSpan Clip = TimeSpan.FromSeconds(12);
    private const double Track = 700;

    private static TimeSpan Sec(double seconds) => TimeSpan.FromSeconds(seconds);

    [Fact]
    public void TimeToX_MapsTheClipOntoTheWholeTrack()
    {
        Assert.Equal(0d, TrimGeometry.TimeToX(TimeSpan.Zero, Clip, Track));
        Assert.Equal(Track, TrimGeometry.TimeToX(Clip, Clip, Track));
        Assert.Equal(Track / 2, TrimGeometry.TimeToX(Sec(6), Clip, Track), 6);
    }

    [Fact]
    public void TimeToX_ClampsTimesOutsideTheClip()
    {
        Assert.Equal(0d, TrimGeometry.TimeToX(Sec(-5), Clip, Track));
        Assert.Equal(Track, TrimGeometry.TimeToX(Sec(99), Clip, Track));
    }

    /// <summary>
    /// A control is laid out before its media has opened, so both of these are
    /// reached in the ordinary run of things -- not just in a degenerate case.
    /// </summary>
    [Fact]
    public void TimeToX_ReturnsZeroRatherThanDividingByZero()
    {
        Assert.Equal(0d, TrimGeometry.TimeToX(Sec(4), TimeSpan.Zero, Track));
        Assert.Equal(0d, TrimGeometry.TimeToX(Sec(4), Clip, 0));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(6)]
    [InlineData(12)]
    public void XToTime_RoundTripsTimeToX(double seconds)
    {
        var x = TrimGeometry.TimeToX(Sec(seconds), Clip, Track);

        Assert.Equal(seconds, TrimGeometry.XToTime(x, Clip, Track).TotalSeconds, 6);
    }

    [Fact]
    public void XToTime_ClampsBeyondEitherEdge()
    {
        Assert.Equal(TimeSpan.Zero, TrimGeometry.XToTime(-40, Clip, Track));
        Assert.Equal(Clip, TrimGeometry.XToTime(Track + 40, Clip, Track));
    }

    [Fact]
    public void XToTime_ReturnsZeroRatherThanDividingByZero()
    {
        Assert.Equal(TimeSpan.Zero, TrimGeometry.XToTime(100, TimeSpan.Zero, Track));
        Assert.Equal(TimeSpan.Zero, TrimGeometry.XToTime(100, Clip, 0));
    }

    [Fact]
    public void ClampStart_StopsShortOfTheEndHandleRatherThanCrossingIt()
    {
        var (start, end) = TrimGeometry.ClampStart(Sec(11.9), Sec(6), Clip);

        Assert.Equal(Sec(6) - TrimGeometry.MinimumRange, start);
        Assert.Equal(Sec(6), end);
    }

    [Fact]
    public void ClampStart_NeverGoesBeforeTheBeginningOfTheClip()
    {
        var (start, end) = TrimGeometry.ClampStart(Sec(-3), Sec(6), Clip);

        Assert.Equal(TimeSpan.Zero, start);
        Assert.Equal(Sec(6), end);
    }

    /// <summary>
    /// The only case where the handle that is not being dragged may move: there
    /// is no room left for it to stay put.
    /// </summary>
    [Fact]
    public void ClampStart_PushesAnEndHandlePinnedAgainstZero()
    {
        var (start, end) = TrimGeometry.ClampStart(Sec(4), Sec(0.2), Clip);

        Assert.Equal(TimeSpan.Zero, start);
        Assert.Equal(TrimGeometry.MinimumRange, end);
    }

    [Fact]
    public void ClampEnd_StopsShortOfTheStartHandleRatherThanCrossingIt()
    {
        var (start, end) = TrimGeometry.ClampEnd(Sec(6), Sec(6.1), Clip);

        Assert.Equal(Sec(6), start);
        Assert.Equal(Sec(6) + TrimGeometry.MinimumRange, end);
    }

    [Fact]
    public void ClampEnd_NeverGoesPastTheEndOfTheClip()
    {
        var (start, end) = TrimGeometry.ClampEnd(Sec(6), Sec(99), Clip);

        Assert.Equal(Sec(6), start);
        Assert.Equal(Clip, end);
    }

    [Fact]
    public void ClampEnd_PullsAStartHandlePinnedAgainstTheEnd()
    {
        var (start, end) = TrimGeometry.ClampEnd(Sec(11.9), Sec(11.95), Clip);

        Assert.Equal(Clip, end);
        Assert.Equal(Clip - TrimGeometry.MinimumRange, start);
    }

    /// <summary>
    /// Dragging one handle must leave the other exactly where it was; an earlier
    /// shape of this code nudged it by a tick each time.
    /// </summary>
    [Fact]
    public void ClampingOneHandleLeavesTheOtherAlone()
    {
        Assert.Equal((Sec(2), Sec(9)), TrimGeometry.ClampStart(Sec(2), Sec(9), Clip));
        Assert.Equal((Sec(2), Sec(9)), TrimGeometry.ClampEnd(Sec(2), Sec(9), Clip));
    }

    [Fact]
    public void ClampingAnUnreadableClipYieldsAnEmptyRange()
    {
        Assert.Equal((TimeSpan.Zero, TimeSpan.Zero), TrimGeometry.ClampStart(Sec(1), Sec(2), TimeSpan.Zero));
        Assert.Equal((TimeSpan.Zero, TimeSpan.Zero), TrimGeometry.ClampEnd(Sec(1), Sec(2), TimeSpan.Zero));
    }
}
