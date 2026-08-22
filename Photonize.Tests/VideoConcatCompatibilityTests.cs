using System.Globalization;
using Photonize.Helpers;
using Xunit;

namespace Photonize.Tests;

public class VideoConcatCompatibilityTests
{
    private static VideoFormatFingerprint Clip(
        string name = "a.mp4",
        string codec = "h264",
        int width = 1920,
        int height = 1080,
        string pixelFormat = "yuv420p",
        double frameRate = 30,
        int rotation = 0,
        bool hasAudio = true,
        string audioCodec = "aac",
        int sampleRate = 48000,
        int channels = 2) =>
        new(name, codec, width, height, pixelFormat, frameRate, rotation,
            hasAudio, audioCodec, sampleRate, channels);

    /// <summary>
    /// The mismatch reason, asserted to exist. Keeps the nullable handling in one
    /// place rather than sprinkling ! through every test.
    /// </summary>
    private static string Mismatch(params VideoFormatFingerprint[] clips)
    {
        var reason = VideoConcatCompatibility.DescribeMismatch(clips);
        Assert.NotNull(reason);
        return reason!;
    }

    [Fact]
    public void IdenticalClipsAreCompatible()
    {
        Assert.True(VideoConcatCompatibility.AreConcatCompatible(new[] { Clip(), Clip("b.mp4") }));
    }

    [Fact]
    public void ASingleClipIsAlwaysCompatible()
    {
        Assert.True(VideoConcatCompatibility.AreConcatCompatible(new[] { Clip() }));
    }

    [Fact]
    public void DifferingCodecIsIncompatible()
    {
        var mismatch = Mismatch(Clip(), Clip("b.mp4", codec: "hevc"));

        Assert.Contains("codec", mismatch);
    }

    [Fact]
    public void DifferingResolutionIsIncompatible()
    {
        var mismatch = Mismatch(Clip(), Clip("b.mp4", width: 1280, height: 720));

        Assert.Contains("resolution", mismatch);
    }

    [Fact]
    public void DifferingPixelFormatIsIncompatible()
    {
        var mismatch = Mismatch(Clip(), Clip("b.mp4", pixelFormat: "yuv422p10le"));

        Assert.Contains("pixel format", mismatch);
    }

    [Fact]
    public void TheSameFrameRateWrittenTwoWaysIsCompatible()
    {
        // 30000/1001 and 29.97 are the same rate; rounding to 2dp must not split them.
        Assert.True(VideoConcatCompatibility.AreConcatCompatible(
            new[] { Clip(frameRate: 30000d / 1001d), Clip("b.mp4", frameRate: 29.97) }));
    }

    [Fact]
    public void TwentyNineNineSevenAndThirtyAreIncompatible()
    {
        var mismatch = Mismatch(Clip(frameRate: 29.97), Clip("b.mp4", frameRate: 30));

        Assert.Contains("frame rate", mismatch);
    }

    [Fact]
    public void DifferingRotationIsIncompatible()
    {
        // The iPhone case: a portrait clip carries a rotation matrix, and copying
        // it alongside landscape footage rotates the landscape footage too.
        var mismatch = Mismatch(Clip(), Clip("b.mp4", rotation: 90));

        Assert.Contains("rotation", mismatch);
    }

    [Fact]
    public void ClipsThatAreAllSilentAreCompatible()
    {
        Assert.True(VideoConcatCompatibility.AreConcatCompatible(
            new[] { Clip(hasAudio: false), Clip("b.mp4", hasAudio: false) }));
    }

    [Fact]
    public void OneSilentClipAmongTalkiesIsIncompatible()
    {
        // Verified against real ffmpeg: a -c copy concat of these produces a file
        // whose video runs the full length but whose audio stops at the seam.
        var mismatch = Mismatch(Clip(), Clip("quiet.mp4", hasAudio: false), Clip("c.mp4"));

        Assert.Contains("audio", mismatch);
        Assert.Contains("quiet.mp4", mismatch);
    }

    [Theory]
    [InlineData("mp3", 48000, 2, "codec")]
    [InlineData("aac", 44100, 2, "sample rate")]
    [InlineData("aac", 48000, 1, "channel")]
    public void DifferingAudioParametersAreIncompatible(string codec, int rate, int channels, string expected)
    {
        var mismatch = Mismatch(Clip(), Clip("b.mp4", audioCodec: codec, sampleRate: rate, channels: channels));

        Assert.Contains(expected, mismatch);
    }

    [Fact]
    public void AudioParametersAreIgnoredWhenNoClipHasAudio()
    {
        // Stale audio fields on a silent clip must not create a phantom mismatch.
        Assert.True(VideoConcatCompatibility.AreConcatCompatible(new[]
        {
            Clip(hasAudio: false, audioCodec: "aac", sampleRate: 48000),
            Clip("b.mp4", hasAudio: false, audioCodec: "mp3", sampleRate: 44100)
        }));
    }

    [Fact]
    public void ContainerDifferencesAloneDoNotForceAReencode()
    {
        // .mp4 and .mov both hold h264/aac and concat-copy into each other.
        Assert.True(VideoConcatCompatibility.AreConcatCompatible(
            new[] { Clip("a.mov"), Clip("b.mp4") }));
    }

    [Fact]
    public void FrameRateMismatchIsReportedWithAnInvariantDecimalSeparator()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            // On de-DE a naive format writes "29,97", which would also corrupt the
            // filter graph built from the same value.
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            var mismatch = Mismatch(Clip(frameRate: 29.97), Clip("b.mp4", frameRate: 30));

            Assert.Contains("29.97", mismatch);
            Assert.DoesNotContain("29,97", mismatch);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
