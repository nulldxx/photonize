using System.Text;
using Photonize.Services;
using Xunit;

namespace Photonize.Tests;

/// <summary>
/// Exercises the TIFF/IFD walk that lifts a JPEG preview out of a DNG. Fixtures are
/// synthesised byte-by-byte rather than checked in: a real DNG is tens of megabytes,
/// and hand-built files can pose the awkward cases (big-endian, raw sensor data that
/// also claims JPEG compression, offsets past the end) that camera files will not.
/// </summary>
public class DngPreviewExtractorTests
{
    // TIFF field types.
    private const ushort TypeShort = 3;
    private const ushort TypeLong = 4;

    /// <summary>
    /// One image inside the synthetic DNG. The defaults describe an ordinary preview;
    /// tests override <see cref="SubfileType"/> or <see cref="Photometric"/> to pose as
    /// raw sensor data instead.
    /// </summary>
    private sealed record PreviewSpec(
        uint Width,
        byte[] Data,
        uint SubfileType = 1,
        ushort Compression = 7,
        ushort? Photometric = 6,
        bool UseJpegInterchangeTags = false,
        uint? OffsetOverride = null);

    // ---------------------------------------------------------------- happy paths

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReadsAPreviewFromEitherByteOrder(bool littleEndian)
    {
        var jpeg = Payload("preview");
        var dng = BuildDng(littleEndian, orientation: 1, new PreviewSpec(1024, jpeg));

        var preview = Extract(dng);

        Assert.NotNull(preview);
        Assert.Equal(jpeg, preview!.JpegBytes);
    }

    [Fact]
    public void PrefersTheWidestPreview()
    {
        var thumbnail = Payload("small");
        var full = Payload("large");
        var dng = BuildDng(true, orientation: 1,
            new PreviewSpec(160, thumbnail),
            new PreviewSpec(1024, full));

        var preview = Extract(dng);

        Assert.Equal(full, preview!.JpegBytes);
    }

    [Fact]
    public void ReadsAPreviewStoredUnderTheJpegInterchangeTags()
    {
        var jpeg = Payload("interchange");
        var dng = BuildDng(true, orientation: 1,
            new PreviewSpec(1024, jpeg, UseJpegInterchangeTags: true));

        Assert.Equal(jpeg, Extract(dng)!.JpegBytes);
    }

    [Fact]
    public void SurfacesTheOrientation()
    {
        var dng = BuildDng(true, orientation: 6, new PreviewSpec(1024, Payload("p")));

        Assert.Equal(6, Extract(dng)!.Orientation);
    }

    [Fact]
    public void DefaultsToUprightWhenOrientationIsOutOfRange()
    {
        var dng = BuildDng(true, orientation: 99, new PreviewSpec(1024, Payload("p")));

        Assert.Equal(1, Extract(dng)!.Orientation);
    }

    // ------------------------------------------------------- not mistaking raw data

    [Fact]
    public void IgnoresRawSensorDataThatAlsoClaimsJpegCompression()
    {
        // Lossless-JPEG raw reports compression 7 and is the widest image in the file.
        // Only NewSubfileType tells it apart from a preview, so picking on size alone
        // would hand back something no JPEG decoder can read.
        var raw = Payload("raw-sensor-data");
        var preview = Payload("preview");
        var dng = BuildDng(true, orientation: 1,
            new PreviewSpec(8000, raw, SubfileType: 0, Photometric: 32803),
            new PreviewSpec(1024, preview));

        Assert.Equal(preview, Extract(dng)!.JpegBytes);
    }

    [Fact]
    public void IgnoresAnImageWithANonRgbPhotometricInterpretation()
    {
        var dng = BuildDng(true, orientation: 1,
            new PreviewSpec(1024, Payload("cfa"), Photometric: 32803));

        Assert.Null(Extract(dng));
    }

    [Fact]
    public void IgnoresAnUncompressedImage()
    {
        var dng = BuildDng(true, orientation: 1,
            new PreviewSpec(1024, Payload("tiff"), Compression: 1));

        Assert.Null(Extract(dng));
    }

    [Fact]
    public void ReturnsNullWhenTheFileHoldsNoPreview()
    {
        var dng = BuildDng(true, orientation: 1,
            new PreviewSpec(8000, Payload("raw"), SubfileType: 0, Photometric: 32803));

        Assert.Null(Extract(dng));
    }

    // ------------------------------------------------------------- malformed input

    [Fact]
    public void ReturnsNullForAnEmptyStream()
    {
        Assert.Null(DngPreviewExtractor.ExtractLargestPreview(new MemoryStream()));
    }

    [Fact]
    public void ReturnsNullForAJpegHandedInByMistake()
    {
        var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46 };

        Assert.Null(Extract(jpeg));
    }

    [Fact]
    public void ReturnsNullForABadMagicNumber()
    {
        var dng = BuildDng(true, orientation: 1, new PreviewSpec(1024, Payload("p")));
        dng[2] = 0x63; // 42 -> something else

        Assert.Null(Extract(dng));
    }

    [Fact]
    public void ReturnsNullForATruncatedFile()
    {
        var dng = BuildDng(true, orientation: 1, new PreviewSpec(1024, Payload("preview")));

        Assert.Null(Extract(dng.Take(dng.Length / 2).ToArray()));
    }

    [Fact]
    public void ReturnsNullWhenThePreviewOffsetRunsPastTheEndOfTheFile()
    {
        var dng = BuildDng(true, orientation: 1,
            new PreviewSpec(1024, Payload("p"), OffsetOverride: 0x7FFFFF00));

        Assert.Null(Extract(dng));
    }

    [Fact]
    public void ReturnsNullWhenTheFirstIfdOffsetIsGarbage()
    {
        var dng = BuildDng(true, orientation: 1, new PreviewSpec(1024, Payload("p")));
        dng[4] = 0xFF;
        dng[5] = 0xFF;
        dng[6] = 0xFF;
        dng[7] = 0x7F;

        Assert.Null(Extract(dng));
    }

    [Fact]
    public void SurvivesAnIfdChainThatPointsAtItself()
    {
        var dng = BuildDng(true, orientation: 1, new PreviewSpec(1024, Payload("preview")));
        // IFD0 sits at offset 8 and has 2 entries, so its "next IFD" field is at
        // 8 + 2 + 2*12 = 34. Aim it back at IFD0.
        dng[34] = 8;

        // The visited-offset guard must stop the walk rather than spin.
        Assert.NotNull(Extract(dng));
    }

    // ------------------------------------------------------------------ efficiency

    [Fact]
    public void ReadsOnlyTheIfdTablesAndThePreviewItself()
    {
        // A DNG is routinely 25-100 MB. Slurping the file to find a 1 MB preview would
        // make a folder of them unusable, so assert the reads stay proportional to the
        // preview rather than the file.
        var jpeg = Payload("preview");
        var dng = BuildDng(true, orientation: 1, new PreviewSpec(1024, jpeg));
        var padded = new byte[4 * 1024 * 1024];
        dng.CopyTo(padded, 0);

        using var stream = new CountingStream(new MemoryStream(padded));
        var preview = DngPreviewExtractor.ExtractLargestPreview(stream);

        Assert.Equal(jpeg, preview!.JpegBytes);
        Assert.True(stream.BytesRead < jpeg.Length + 4096,
            $"read {stream.BytesRead} bytes to extract a {jpeg.Length} byte preview");
    }

    // ---------------------------------------------------------------------- helpers

    private static DngPreview? Extract(byte[] bytes) =>
        DngPreviewExtractor.ExtractLargestPreview(new MemoryStream(bytes));

    /// <summary>Stand-in for JPEG bytes: the extractor never parses them, only locates
    /// them, so a recognisable marker padded to a realistic length is enough.</summary>
    private static byte[] Payload(string marker) =>
        Encoding.ASCII.GetBytes(marker.PadRight(256, '.'));

    /// <summary>
    /// Lays out a minimal TIFF: header, IFD0 (orientation + a SubIFDs pointer), one
    /// sub-IFD per image, then the image byte blobs. Every value is small enough to sit
    /// inside its 12-byte entry except the SubIFDs array when there is more than one.
    /// </summary>
    private static byte[] BuildDng(bool littleEndian, int orientation, params PreviewSpec[] previews)
    {
        static int IfdSize(int entryCount) => 2 + entryCount * 12 + 4;

        var subEntryCounts = previews.Select(p => p.Photometric.HasValue ? 6 : 5).ToArray();

        const int ifd0Offset = 8;
        var cursor = ifd0Offset + IfdSize(2);

        var subIfdOffsets = new int[previews.Length];
        for (var i = 0; i < previews.Length; i++)
        {
            subIfdOffsets[i] = cursor;
            cursor += IfdSize(subEntryCounts[i]);
        }

        var subIfdArrayOffset = cursor;
        if (previews.Length > 1)
            cursor += 4 * previews.Length;

        var blobOffsets = new int[previews.Length];
        for (var i = 0; i < previews.Length; i++)
        {
            blobOffsets[i] = cursor;
            cursor += previews[i].Data.Length;
        }

        var buffer = new byte[cursor];

        void PutU16(int at, ushort value)
        {
            buffer[at] = littleEndian ? (byte)value : (byte)(value >> 8);
            buffer[at + 1] = littleEndian ? (byte)(value >> 8) : (byte)value;
        }

        void PutU32(int at, uint value)
        {
            for (var i = 0; i < 4; i++)
                buffer[at + i] = (byte)(value >> (8 * (littleEndian ? i : 3 - i)));
        }

        // TIFF left-justifies a value inside the 4-byte field, so a SHORT occupies the
        // first two bytes whichever way round the file is.
        int PutEntry(int at, ushort tag, ushort type, uint count, uint value)
        {
            PutU16(at, tag);
            PutU16(at + 2, type);
            PutU32(at + 4, count);
            if (type == TypeShort && count == 1)
                PutU16(at + 8, (ushort)value);
            else
                PutU32(at + 8, value);
            return at + 12;
        }

        buffer[0] = buffer[1] = littleEndian ? (byte)0x49 : (byte)0x4D;
        PutU16(2, 42);
        PutU32(4, ifd0Offset);

        var p = ifd0Offset;
        PutU16(p, 2);
        p += 2;
        p = PutEntry(p, 0x0112, TypeShort, 1, (uint)orientation);
        p = previews.Length == 1
            ? PutEntry(p, 0x014A, TypeLong, 1, (uint)subIfdOffsets[0])
            : PutEntry(p, 0x014A, TypeLong, (uint)previews.Length, (uint)subIfdArrayOffset);
        PutU32(p, 0); // no chained IFD

        if (previews.Length > 1)
            for (var i = 0; i < previews.Length; i++)
                PutU32(subIfdArrayOffset + i * 4, (uint)subIfdOffsets[i]);

        for (var i = 0; i < previews.Length; i++)
        {
            var spec = previews[i];
            var q = subIfdOffsets[i];
            PutU16(q, (ushort)subEntryCounts[i]);
            q += 2;
            q = PutEntry(q, 0x00FE, TypeLong, 1, spec.SubfileType);
            q = PutEntry(q, 0x0100, TypeLong, 1, spec.Width);
            q = PutEntry(q, 0x0103, TypeShort, 1, spec.Compression);
            if (spec.Photometric.HasValue)
                q = PutEntry(q, 0x0106, TypeShort, 1, spec.Photometric.Value);

            var offsetTag = spec.UseJpegInterchangeTags ? (ushort)0x0201 : (ushort)0x0111;
            var lengthTag = spec.UseJpegInterchangeTags ? (ushort)0x0202 : (ushort)0x0117;
            q = PutEntry(q, offsetTag, TypeLong, 1, spec.OffsetOverride ?? (uint)blobOffsets[i]);
            q = PutEntry(q, lengthTag, TypeLong, 1, (uint)spec.Data.Length);
            PutU32(q, 0);

            spec.Data.CopyTo(buffer, blobOffsets[i]);
        }

        return buffer;
    }

    /// <summary>A read-only pass-through that tallies how many bytes actually left the stream.</summary>
    private sealed class CountingStream : Stream
    {
        private readonly Stream _inner;

        public CountingStream(Stream inner) => _inner = inner;

        public long BytesRead { get; private set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = _inner.Read(buffer, offset, count);
            BytesRead += read;
            return read;
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => _inner.Position = value; }
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void Flush() => _inner.Flush();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
