using System.IO;

namespace Photonize.Services;

/// <summary>
/// A JPEG preview lifted out of a DNG, together with the orientation that has to be
/// applied to it. DNG stores its previews in the raw image's own orientation, so a
/// portrait frame comes back sideways unless <see cref="Orientation"/> is honoured.
/// </summary>
/// <param name="JpegBytes">The preview, byte-for-byte as stored — a complete JPEG.</param>
/// <param name="Orientation">The EXIF/TIFF orientation value 1-8; 1 means upright.</param>
public sealed record DngPreview(byte[] JpegBytes, int Orientation);

/// <summary>
/// Pulls the embedded JPEG preview out of an Adobe DNG by walking the file's TIFF
/// IFDs. A DNG is a TIFF whose sub-IFDs hold the raw sensor data alongside one or
/// more ordinary JPEG previews; this reads the largest of those previews, which is
/// what Explorer, Finder and Lightroom's grid all display.
///
/// Doing it here rather than handing the file to WIC keeps the behaviour identical on
/// every machine. Windows ships a native DNG codec that reads exactly this preview,
/// but Microsoft's Raw Image Extension supersedes it when installed and demosaics the
/// full sensor instead — correct, but far too slow for a grid of tiles — and on a
/// machine with neither, decoding simply fails.
///
/// Deliberately free of WPF types, following <c>DragPayloadBuilder</c>, so the parsing
/// rules can be unit tested without a UI.
/// </summary>
public static class DngPreviewExtractor
{
    // TIFF 6.0 / DNG tags. Only the handful needed to find a preview.
    private const ushort TagNewSubfileType = 0x00FE;
    private const ushort TagImageWidth = 0x0100;
    private const ushort TagCompression = 0x0103;
    private const ushort TagPhotometricInterpretation = 0x0106;
    private const ushort TagStripOffsets = 0x0111;
    private const ushort TagOrientation = 0x0112;
    private const ushort TagStripByteCounts = 0x0117;
    private const ushort TagSubIfds = 0x014A;
    private const ushort TagJpegInterchangeFormat = 0x0201;
    private const ushort TagJpegInterchangeFormatLength = 0x0202;

    /// <summary>NewSubfileType bit meaning "a reduced-resolution version of another image".</summary>
    private const uint SubfileTypeReducedResolution = 1;

    private const ushort CompressionOldJpeg = 6;
    private const ushort CompressionJpeg = 7;

    private const ushort PhotometricRgb = 2;
    private const ushort PhotometricYCbCr = 6;

    // Sanity ceilings. A malformed or hostile file must fall out rather than send us
    // chasing offsets around a 100 MB buffer.
    private const int MaxIfds = 64;
    private const int MaxEntriesPerIfd = 512;
    private const int MaxValuesPerTag = 256;
    private const int MaxPreviewBytes = 64 * 1024 * 1024;

    /// <summary>
    /// Reads the largest embedded JPEG preview from a DNG, or null when the stream is
    /// not a TIFF-based file, is truncated or malformed, or holds no preview at all.
    /// Never throws.
    ///
    /// Only the IFD tables and the preview's own byte range are read, so this costs a
    /// few small seeks even on a 100 MB raw file.
    /// </summary>
    public static DngPreview? ExtractLargestPreview(Stream stream)
    {
        if (stream is not { CanRead: true, CanSeek: true })
            return null;

        try
        {
            var length = stream.Length;

            var header = ReadAt(stream, 0, 8);
            if (header == null)
                return null;

            // "II" little-endian or "MM" big-endian, then the magic 42.
            bool littleEndian;
            if (header[0] == 0x49 && header[1] == 0x49)
                littleEndian = true;
            else if (header[0] == 0x4D && header[1] == 0x4D)
                littleEndian = false;
            else
                return null;

            if (ToUInt16(header, 2, littleEndian) != 42)
                return null;

            var visited = new HashSet<long>();
            var pending = new Queue<long>();
            pending.Enqueue(ToUInt32(header, 4, littleEndian));

            var orientation = 1;
            var orientationFound = false;
            PreviewCandidate? best = null;

            while (pending.Count > 0 && visited.Count < MaxIfds)
            {
                var ifdOffset = pending.Dequeue();
                if (ifdOffset <= 0 || ifdOffset >= length || !visited.Add(ifdOffset))
                    continue;

                var entries = ReadIfd(stream, ifdOffset, littleEndian, out var nextIfd);
                if (entries == null)
                    continue;

                if (nextIfd > 0)
                    pending.Enqueue(nextIfd);

                foreach (var subIfd in ReadValues(stream, entries, TagSubIfds, littleEndian))
                    pending.Enqueue(subIfd);

                // IFD0 is dequeued first, so the first orientation seen is the file's.
                if (!orientationFound && TryReadScalar(stream, entries, TagOrientation, littleEndian, out var value))
                {
                    orientation = value is >= 1 and <= 8 ? (int)value : 1;
                    orientationFound = true;
                }

                var candidate = ReadPreviewCandidate(stream, entries, littleEndian, length);
                if (candidate != null && (best == null || candidate.Width > best.Width))
                    best = candidate;
            }

            if (best == null)
                return null;

            var jpeg = ReadAt(stream, best.Offset, best.Length);
            return jpeg == null ? null : new DngPreview(jpeg, orientation);
        }
        catch (Exception)
        {
            // A corrupt or truncated DNG is a routine thing to meet on disk, not an
            // error worth propagating — the caller falls back to WIC.
            return null;
        }
    }

    /// <summary>The location and size of one candidate preview inside the file.</summary>
    private sealed record PreviewCandidate(long Offset, int Length, uint Width);

    /// <summary>One IFD entry: the tag, its type, how many values, and the 4 value bytes
    /// (which hold the value itself when it fits, or an offset to it when it does not).</summary>
    private readonly record struct IfdEntry(ushort Tag, ushort Type, uint Count, byte[] Value);

    /// <summary>
    /// Decides whether an IFD describes a usable preview. The NewSubfileType check is
    /// what keeps us off the raw sensor data: lossless-JPEG raw also reports
    /// compression 7, and it is frequently the widest image in the file, so picking on
    /// size alone would hand back something no JPEG decoder can read.
    /// </summary>
    private static PreviewCandidate? ReadPreviewCandidate(
        Stream stream, Dictionary<ushort, IfdEntry> entries, bool littleEndian, long fileLength)
    {
        if (!TryReadScalar(stream, entries, TagNewSubfileType, littleEndian, out var subfileType) ||
            (subfileType & SubfileTypeReducedResolution) == 0)
            return null;

        if (!TryReadScalar(stream, entries, TagCompression, littleEndian, out var compression) ||
            (compression != CompressionJpeg && compression != CompressionOldJpeg))
            return null;

        // Absent is tolerated; present and non-RGB means it is not a plain JPEG.
        if (TryReadScalar(stream, entries, TagPhotometricInterpretation, littleEndian, out var photometric) &&
            photometric != PhotometricRgb && photometric != PhotometricYCbCr)
            return null;

        // TIFF/EP thumbnails use JPEGInterchangeFormat; DNG previews use a single strip.
        var offsets = ReadValues(stream, entries, TagJpegInterchangeFormat, littleEndian);
        var lengths = ReadValues(stream, entries, TagJpegInterchangeFormatLength, littleEndian);
        if (offsets.Count != 1 || lengths.Count != 1)
        {
            offsets = ReadValues(stream, entries, TagStripOffsets, littleEndian);
            lengths = ReadValues(stream, entries, TagStripByteCounts, littleEndian);
        }

        // A JPEG split across several strips is not something a decoder will take.
        if (offsets.Count != 1 || lengths.Count != 1)
            return null;

        long offset = offsets[0];
        long size = lengths[0];
        if (offset <= 0 || size <= 0 || size > MaxPreviewBytes || offset + size > fileLength)
            return null;

        TryReadScalar(stream, entries, TagImageWidth, littleEndian, out var width);
        return new PreviewCandidate(offset, (int)size, width);
    }

    private static Dictionary<ushort, IfdEntry>? ReadIfd(
        Stream stream, long offset, bool littleEndian, out long nextIfd)
    {
        nextIfd = 0;

        var countBytes = ReadAt(stream, offset, 2);
        if (countBytes == null)
            return null;

        int count = ToUInt16(countBytes, 0, littleEndian);
        if (count is <= 0 or > MaxEntriesPerIfd)
            return null;

        var table = ReadAt(stream, offset + 2, count * 12 + 4);
        if (table == null)
            return null;

        var entries = new Dictionary<ushort, IfdEntry>(count);
        for (var i = 0; i < count; i++)
        {
            var p = i * 12;
            var entry = new IfdEntry(
                ToUInt16(table, p, littleEndian),
                ToUInt16(table, p + 2, littleEndian),
                ToUInt32(table, p + 4, littleEndian),
                table[(p + 8)..(p + 12)]);
            entries[entry.Tag] = entry;
        }

        nextIfd = ToUInt32(table, count * 12, littleEndian);
        return entries;
    }

    /// <summary>
    /// All values of a tag, widened to uint. Returns empty for an absent tag, a type we
    /// do not handle, or a value block that does not fit inside the file.
    /// </summary>
    private static IReadOnlyList<uint> ReadValues(
        Stream stream, Dictionary<ushort, IfdEntry> entries, ushort tag, bool littleEndian)
    {
        if (!entries.TryGetValue(tag, out var entry))
            return Array.Empty<uint>();

        var size = TypeSize(entry.Type);
        if (size == 0 || entry.Count == 0 || entry.Count > MaxValuesPerTag)
            return Array.Empty<uint>();

        var total = (long)size * entry.Count;
        byte[] data;
        if (total <= 4)
        {
            data = entry.Value;
        }
        else
        {
            var block = ReadAt(stream, ToUInt32(entry.Value, 0, littleEndian), (int)total);
            if (block == null)
                return Array.Empty<uint>();
            data = block;
        }

        var values = new uint[entry.Count];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = size switch
            {
                1 => data[i],
                2 => ToUInt16(data, i * 2, littleEndian),
                _ => ToUInt32(data, i * 4, littleEndian)
            };
        }

        return values;
    }

    private static bool TryReadScalar(
        Stream stream, Dictionary<ushort, IfdEntry> entries, ushort tag, bool littleEndian, out uint value)
    {
        var values = ReadValues(stream, entries, tag, littleEndian);
        value = values.Count > 0 ? values[0] : 0;
        return values.Count > 0;
    }

    /// <summary>Bytes per value for the TIFF types we read; 0 for anything else.</summary>
    private static int TypeSize(ushort type) => type switch
    {
        1 or 2 or 6 or 7 => 1,   // BYTE, ASCII, SBYTE, UNDEFINED
        3 or 8 => 2,             // SHORT, SSHORT
        4 or 9 or 13 => 4,       // LONG, SLONG, IFD
        _ => 0
    };

    /// <summary>Reads exactly <paramref name="count"/> bytes at <paramref name="offset"/>,
    /// or null when that range falls outside the stream.</summary>
    private static byte[]? ReadAt(Stream stream, long offset, int count)
    {
        if (offset < 0 || count <= 0 || offset + count > stream.Length)
            return null;

        stream.Position = offset;
        var buffer = new byte[count];
        stream.ReadExactly(buffer);
        return buffer;
    }

    private static ushort ToUInt16(byte[] bytes, int index, bool littleEndian) =>
        littleEndian
            ? (ushort)(bytes[index] | (bytes[index + 1] << 8))
            : (ushort)((bytes[index] << 8) | bytes[index + 1]);

    private static uint ToUInt32(byte[] bytes, int index, bool littleEndian) =>
        littleEndian
            ? (uint)(bytes[index] | (bytes[index + 1] << 8) | (bytes[index + 2] << 16) | (bytes[index + 3] << 24))
            : (uint)((bytes[index] << 24) | (bytes[index + 1] << 16) | (bytes[index + 2] << 8) | bytes[index + 3]);
}
