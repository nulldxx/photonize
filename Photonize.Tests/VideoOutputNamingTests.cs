using System.Globalization;
using Photonize.Helpers;
using Xunit;

namespace Photonize.Tests;

public class VideoOutputNamingTests
{
    private static Func<string, bool> Existing(params string[] paths) =>
        candidate => paths.Contains(candidate, StringComparer.OrdinalIgnoreCase);

    private static Func<string, bool> NothingExists() => _ => false;

    [Fact]
    public void SlicePattern_UsesPartInfixAndThreeDigitPlaceholder()
    {
        var pattern = VideoOutputNaming.SlicePattern(@"C:\v\clip.mp4", NothingExists());

        Assert.Equal(@"C:\v\clip-part-%03d.mp4", pattern);
    }

    [Theory]
    [InlineData(".mov")]
    [InlineData(".m4v")]
    [InlineData(".MP4")]
    [InlineData(".mkv")]
    public void SlicePattern_KeepsSourceExtension(string extension)
    {
        // Parts are stream-copied, so a .mov holding ProRes cannot become a .mp4.
        var pattern = VideoOutputNaming.SlicePattern($@"C:\v\clip{extension}", NothingExists());

        Assert.EndsWith($"-part-%03d{extension}", pattern);
    }

    [Fact]
    public void SlicePattern_StepsToFreshFamilyWhenFirstPartExists()
    {
        var pattern = VideoOutputNaming.SlicePattern(
            @"C:\v\clip.mp4",
            Existing(@"C:\v\clip-part-001.mp4"));

        Assert.Equal(@"C:\v\clip-2-part-%03d.mp4", pattern);
    }

    [Fact]
    public void SlicePattern_KeepsSteppingUntilAFamilyIsFree()
    {
        var pattern = VideoOutputNaming.SlicePattern(
            @"C:\v\clip.mp4",
            Existing(@"C:\v\clip-part-001.mp4", @"C:\v\clip-2-part-001.mp4"));

        Assert.Equal(@"C:\v\clip-3-part-%03d.mp4", pattern);
    }

    [Theory]
    [InlineData(@"C:\v\clip.mp4", "mp4")]
    [InlineData(@"C:\v\clip.m4v", "mp4")]
    [InlineData(@"C:\v\clip.mov", "mov")]
    [InlineData(@"C:\v\clip.MOV", "mov")]
    [InlineData(@"C:\v\clip.mkv", "matroska")]
    [InlineData(@"C:\v\clip.MKV", "matroska")]
    public void MuxerFor_NamesTheContainerOutright(string path, string muxer)
    {
        // The segment muxer will not infer .m4v, and would write an mp4 into a
        // .mkv given half a chance.
        Assert.Equal(muxer, VideoOutputNaming.MuxerFor(path));
    }

    [Theory]
    [InlineData(@"C:\v\clip.mp4", true)]
    [InlineData(@"C:\v\clip.m4v", true)]
    [InlineData(@"C:\v\clip.mov", true)]
    // -movflags belongs to the mov muxer: handing it to matroska fails the run
    // outright rather than being ignored.
    [InlineData(@"C:\v\clip.mkv", false)]
    public void SupportsFaststart_OnlyForTheMovFamily(string path, bool supported)
    {
        Assert.Equal(supported, VideoOutputNaming.SupportsFaststart(path));
    }

    [Fact]
    public void PartSearchPattern_MatchesEveryPartOfTheFamily()
    {
        var pattern = VideoOutputNaming.SlicePattern(@"C:\v\clip.mp4", NothingExists());

        Assert.Equal("clip-part-*.mp4", VideoOutputNaming.PartSearchPattern(pattern));
    }

    [Fact]
    public void SpliceOutputPath_NamesAfterTheFirstClip()
    {
        var path = VideoOutputNaming.SpliceOutputPath(
            new[] { @"C:\v\a.mp4", @"C:\v\b.mp4" }, reencoded: false, NothingExists());

        Assert.Equal(@"C:\v\a-spliced.mp4", path);
    }

    [Fact]
    public void SpliceOutputPath_SuffixesOnCollision()
    {
        var path = VideoOutputNaming.SpliceOutputPath(
            new[] { @"C:\v\a.mp4", @"C:\v\b.mp4" },
            reencoded: false,
            Existing(@"C:\v\a-spliced.mp4"));

        Assert.Equal(@"C:\v\a-spliced-2.mp4", path);
    }

    [Fact]
    public void SpliceExtension_KeepsSharedContainerWhenCopying()
    {
        var extension = VideoOutputNaming.SpliceExtension(
            new[] { @"C:\v\a.mov", @"C:\v\b.MOV" }, reencoded: false);

        Assert.Equal(".mov", extension);
    }

    [Fact]
    public void SpliceExtension_KeepsMatroskaWhenEveryClipIsMkv()
    {
        // A copy-concat of two .mkv files stays Matroska; only a re-encode, or a
        // mixed selection, forces .mp4.
        var extension = VideoOutputNaming.SpliceExtension(
            new[] { @"C:\v\a.mkv", @"C:\v\b.MKV" }, reencoded: false);

        Assert.Equal(".mkv", extension);
    }

    [Fact]
    public void SpliceExtension_FallsBackToMp4WhenContainersDiffer()
    {
        var extension = VideoOutputNaming.SpliceExtension(
            new[] { @"C:\v\a.mov", @"C:\v\b.mp4" }, reencoded: false);

        Assert.Equal(".mp4", extension);
    }

    [Fact]
    public void SpliceExtension_AlwaysMp4WhenReencoding()
    {
        // A re-encode emits h264/aac, which only .mp4 reliably holds.
        var extension = VideoOutputNaming.SpliceExtension(
            new[] { @"C:\v\a.mov", @"C:\v\b.mov" }, reencoded: true);

        Assert.Equal(".mp4", extension);
    }

    [Theory]
    [InlineData(90, 30, 3)]
    [InlineData(91, 30, 4)]
    [InlineData(30, 30, 1)]
    [InlineData(1, 30, 1)]
    public void ExpectedPartCount_RoundsUp(int durationSeconds, int partLength, int expected)
    {
        Assert.Equal(expected, VideoOutputNaming.ExpectedPartCount(TimeSpan.FromSeconds(durationSeconds), partLength));
    }

    [Fact]
    public void ExpectedPartCount_IsZeroForANonsensePartLength()
    {
        Assert.Equal(0, VideoOutputNaming.ExpectedPartCount(TimeSpan.FromSeconds(60), 0));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void IsValidPartLength_RejectsZeroAndNegative(int seconds)
    {
        Assert.False(VideoOutputNaming.IsValidPartLength(seconds, TimeSpan.FromSeconds(60), out var error));
        Assert.NotEmpty(error);
    }

    [Fact]
    public void IsValidPartLength_AcceptsOneSecond()
    {
        Assert.True(VideoOutputNaming.IsValidPartLength(1, TimeSpan.FromSeconds(60), out _));
    }

    [Fact]
    public void IsValidPartLength_RejectsLengthAtOrBeyondTheClip()
    {
        // Slicing a 60s clip into 60s parts just copies it.
        Assert.False(VideoOutputNaming.IsValidPartLength(60, TimeSpan.FromSeconds(60), out _));
        Assert.False(VideoOutputNaming.IsValidPartLength(90, TimeSpan.FromSeconds(60), out _));
    }

    [Fact]
    public void IsValidPartLength_AcceptsAnyPositiveLengthWhenTheDurationIsUnknown()
    {
        // A multi-clip slice has no single duration to validate against.
        Assert.True(VideoOutputNaming.IsValidPartLength(600, null, out _));
    }

    [Fact]
    public void SlicePattern_LeavesAwkwardCharactersInTheStemAlone()
    {
        // ArgumentList and the concat-list escaping handle these downstream; the
        // naming layer must not mangle them.
        var pattern = VideoOutputNaming.SlicePattern(@"C:\v\Ben's 100% [raw].mp4", NothingExists());

        Assert.Equal(@"C:\v\Ben's 100% [raw]-part-%03d.mp4", pattern);
    }

    // ---- Extract clip ----

    [Fact]
    public void ClipOutputPath_UsesTheClipSuffix()
    {
        var path = VideoOutputNaming.ClipOutputPath(@"C:\v\clip.mp4", reencoded: false, NothingExists());

        Assert.Equal(@"C:\v\clip-clip.mp4", path);
    }

    /// <summary>
    /// A fast extract copies the streams, so it is under the same constraint as a
    /// slice: ProRes in a .mov will not stream-copy into an .mp4.
    /// </summary>
    [Theory]
    [InlineData(".mov", ".mov")]
    [InlineData(".m4v", ".m4v")]
    [InlineData(".mkv", ".mkv")]
    [InlineData(".MOV", ".mov")]
    public void ClipOutputPath_KeepsTheSourceContainerWhenCopying(string extension, string expected)
    {
        var path = VideoOutputNaming.ClipOutputPath($@"C:\v\clip{extension}", reencoded: false, NothingExists());

        Assert.EndsWith($"-clip{expected}", path);
    }

    [Theory]
    [InlineData(".mov")]
    [InlineData(".m4v")]
    [InlineData(".mkv")]
    [InlineData(".mp4")]
    public void ClipOutputPath_IsAlwaysMp4WhenReencoded(string extension)
    {
        var path = VideoOutputNaming.ClipOutputPath($@"C:\v\clip{extension}", reencoded: true, NothingExists());

        Assert.EndsWith("-clip.mp4", path);
    }

    [Fact]
    public void ClipOutputPath_StepsPastAnEarlierExtract()
    {
        var path = VideoOutputNaming.ClipOutputPath(
            @"C:\v\clip.mp4", reencoded: false, Existing(@"C:\v\clip-clip.mp4"));

        Assert.Equal(@"C:\v\clip-clip-2.mp4", path);
    }

    [Fact]
    public void ClipOutputPath_KeepsSteppingUntilANameIsFree()
    {
        var path = VideoOutputNaming.ClipOutputPath(
            @"C:\v\clip.mp4",
            reencoded: false,
            Existing(@"C:\v\clip-clip.mp4", @"C:\v\clip-clip-2.mp4"));

        Assert.Equal(@"C:\v\clip-clip-3.mp4", path);
    }

    /// <summary>
    /// A side effect of the container rule worth keeping: extracting the same
    /// range twice, once each way, produces two files rather than a collision.
    /// </summary>
    [Fact]
    public void ClipOutputPath_FastAndPreciseDoNotCollideForANonMp4Source()
    {
        var fast = VideoOutputNaming.ClipOutputPath(@"C:\v\clip.mov", reencoded: false, NothingExists());
        var precise = VideoOutputNaming.ClipOutputPath(@"C:\v\clip.mov", reencoded: true, NothingExists());

        Assert.NotEqual(fast, precise);
    }

    [Fact]
    public void IsValidTrimRange_AcceptsAnInteriorRange()
    {
        var valid = VideoOutputNaming.IsValidTrimRange(
            TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(11.5), TimeSpan.FromSeconds(30), out var error);

        Assert.True(valid);
        Assert.Empty(error);
    }

    [Fact]
    public void IsValidTrimRange_RejectsARangeShorterThanTheMinimum()
    {
        var valid = VideoOutputNaming.IsValidTrimRange(
            TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(4.2), TimeSpan.FromSeconds(30), out var error);

        Assert.False(valid);
        Assert.Contains("at least", error);
    }

    [Fact]
    public void IsValidTrimRange_RejectsAnInvertedRange()
    {
        var valid = VideoOutputNaming.IsValidTrimRange(
            TimeSpan.FromSeconds(11), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(30), out _);

        Assert.False(valid);
    }

    [Fact]
    public void IsValidTrimRange_RejectsAnEndPastTheClip()
    {
        var valid = VideoOutputNaming.IsValidTrimRange(
            TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(40), TimeSpan.FromSeconds(30), out var error);

        Assert.False(valid);
        Assert.Contains("past the end", error);
    }

    [Fact]
    public void IsValidTrimRange_RejectsAnUnreadableClip()
    {
        var valid = VideoOutputNaming.IsValidTrimRange(
            TimeSpan.Zero, TimeSpan.FromSeconds(4), TimeSpan.Zero, out var error);

        Assert.False(valid);
        Assert.Contains("length", error);
    }

    /// <summary>
    /// ffprobe durations do not round-trip exactly, so a range dragged to the very
    /// end of the clip lands a few ticks past it and must not be rejected.
    /// </summary>
    [Fact]
    public void IsValidTrimRange_ToleratesAnEndAHairPastTheDuration()
    {
        var duration = TimeSpan.FromSeconds(30);
        var valid = VideoOutputNaming.IsValidTrimRange(
            TimeSpan.FromSeconds(4), duration + TimeSpan.FromMilliseconds(8), duration, out _);

        Assert.True(valid);
    }

    [Theory]
    [InlineData(4, "00:04.0")]
    [InlineData(71.5, "01:11.5")]
    [InlineData(0, "00:00.0")]
    public void FormatTimecode_PadsMinutesAndCarriesTenths(double seconds, string expected)
    {
        Assert.Equal(expected, VideoOutputNaming.FormatTimecode(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void FormatTimecode_IncludesHoursForALongClip()
    {
        Assert.Equal("1:05:03.0", VideoOutputNaming.FormatTimecode(TimeSpan.FromSeconds(3903)));
    }

    [Fact]
    public void FormatTrimRange_ReadsAsTheDialogReadout()
    {
        var range = VideoOutputNaming.FormatTrimRange(
            TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(11.5));

        Assert.Equal("00:04.0 \u2192 00:11.5 (7.5s)", range);
    }

    /// <summary>
    /// The read-out is also used in completion messages, so a comma decimal
    /// separator would leak into the UI as well as failing this test.
    /// </summary>
    [Fact]
    public void FormatTrimRange_UsesAnInvariantDecimalPoint()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            Assert.Contains("(7.5s)", VideoOutputNaming.FormatTrimRange(
                TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(11.5)));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
