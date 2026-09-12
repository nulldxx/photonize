using System.Globalization;
using Photonize.Helpers;
using Xunit;

namespace Photonize.Tests;

public class FilmstripLayoutTests
{
    private static TimeSpan Sec(double seconds) => TimeSpan.FromSeconds(seconds);

    [Fact]
    public void SampleTimes_ReturnsTheRequestedNumberOfFrames()
    {
        Assert.Equal(12, FilmstripLayout.SampleTimes(Sec(60), 12).Count);
    }

    /// <summary>
    /// Sampling cell centres rather than edges. Edge sampling puts the first
    /// frame at t=0, which on most clips is a black fade-in, and never reaches
    /// the tail of the clip at all.
    /// </summary>
    [Fact]
    public void SampleTimes_SamplesCellCentresNotEdges()
    {
        var times = FilmstripLayout.SampleTimes(Sec(12), 12);

        Assert.True(times[0] > TimeSpan.Zero);
        Assert.InRange(times[0].TotalSeconds, 0.4, 0.6);
        Assert.InRange(times[^1].TotalSeconds, 11.0, 11.5);
    }

    /// <summary>
    /// A seek past the final frame returns nothing, and hstack is all-or-nothing:
    /// one empty input fails the entire filmstrip.
    /// </summary>
    [Theory]
    [InlineData(0.2)]
    [InlineData(0.5)]
    [InlineData(1)]
    [InlineData(12)]
    [InlineData(3600)]
    public void SampleTimes_StayStrictlyInsideTheClip(double duration)
    {
        var times = FilmstripLayout.SampleTimes(Sec(duration), 12);

        Assert.All(times, time =>
        {
            Assert.True(time >= TimeSpan.Zero);
            Assert.True(time < Sec(duration));
        });
    }

    /// <summary>
    /// Clipping the tail rather than shrinking the span used to collapse the last
    /// few samples of a short clip onto the same instant, so the strip repeated
    /// one frame several times over.
    /// </summary>
    [Theory]
    [InlineData(0.2)]
    [InlineData(0.5)]
    [InlineData(12)]
    [InlineData(3600)]
    public void SampleTimes_AreStrictlyIncreasing(double duration)
    {
        var times = FilmstripLayout.SampleTimes(Sec(duration), 12);

        for (var i = 1; i < times.Count; i++)
        {
            Assert.True(times[i] > times[i - 1], $"frame {i} did not advance");
        }
    }

    [Fact]
    public void SampleTimes_AreEmptyForAnUnreadableClip()
    {
        Assert.Empty(FilmstripLayout.SampleTimes(TimeSpan.Zero, 12));
        Assert.Empty(FilmstripLayout.SampleTimes(Sec(-4), 12));
        Assert.Empty(FilmstripLayout.SampleTimes(Sec(12), 0));
    }

    [Fact]
    public void BuildFilterGraph_ScalesCropsAndStacksEveryInput()
    {
        var graph = FilmstripLayout.BuildFilterGraph(12);

        Assert.Contains("[0:v]scale=160:90:force_original_aspect_ratio=increase", graph);
        Assert.Contains("crop=160:90", graph);
        Assert.Contains("[11:v]", graph);
        Assert.EndsWith("hstack=inputs=12[strip]", graph);
    }

    [Fact]
    public void BuildFilterGraph_LabelsEveryInputExactlyOnce()
    {
        var graph = FilmstripLayout.BuildFilterGraph(5);

        for (var i = 0; i < 5; i++)
        {
            Assert.Contains($"[c{i}]", graph);
        }

        Assert.DoesNotContain("[c5]", graph);
    }

    [Fact]
    public void BuildFilterGraph_RejectsAnEmptyStrip()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FilmstripLayout.BuildFilterGraph(0));
    }

    /// <summary>
    /// On a machine with a comma decimal separator an interpolated number turns
    /// "160" into part of a malformed filter, exactly as BuildConcatFilter warns.
    /// </summary>
    [Fact]
    public void BuildFilterGraph_WritesInvariantNumbers()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var graph = FilmstripLayout.BuildFilterGraph(3);

            Assert.DoesNotContain(",90:force", graph);
            Assert.Contains("scale=160:90", graph);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void CellIndexAt_MapsTimeOntoAStripCell()
    {
        Assert.Equal(0, FilmstripLayout.CellIndexAt(TimeSpan.Zero, Sec(12), 12));
        Assert.Equal(6, FilmstripLayout.CellIndexAt(Sec(6), Sec(12), 12));
        Assert.Equal(11, FilmstripLayout.CellIndexAt(Sec(12), Sec(12), 12));
    }

    [Fact]
    public void CellIndexAt_StaysInsideTheStrip()
    {
        Assert.Equal(0, FilmstripLayout.CellIndexAt(Sec(-5), Sec(12), 12));
        Assert.Equal(11, FilmstripLayout.CellIndexAt(Sec(99), Sec(12), 12));
        Assert.Equal(0, FilmstripLayout.CellIndexAt(Sec(4), TimeSpan.Zero, 12));
    }
}
