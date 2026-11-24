using System.Buffers.Binary;

namespace DfE.CheckPerformanceData.Application.Media.Upload;

/// <summary>
/// Reads the RIFF chunks of a WebP file. StripMetadata keeps only the chunks that draw the image
/// (VP8X, ANIM, ANMF, ALPH, VP8, VP8L), drops EXIF, XMP, ICC profiles and unknown chunks, clears
/// the matching VP8X flags, fixes the RIFF size and drops anything after the declared end. A file
/// whose chunk structure is broken throws <see cref="InvalidDataException"/>.
/// </summary>
public static class WebPContainer
{
    // VP8X flag bits, from the WebP container specification.
    private const byte IccFlag = 0x20;
    private const byte ExifFlag = 0x08;
    private const byte XmpFlag = 0x04;

    private static readonly HashSet<string> ImageChunks = ["VP8X", "ANIM", "ANMF", "ALPH", "VP8 ", "VP8L"];

    public static byte[] StripMetadata(byte[] webp)
    {
        using var output = new MemoryStream(webp.Length);
        output.Write("RIFF\0\0\0\0WEBP"u8);
        foreach (var (id, start, length) in Chunks(webp))
        {
            if (!ImageChunks.Contains(id)) continue;
            var chunk = webp.AsSpan(start, length).ToArray();
            if (id == "VP8X") chunk[8] &= unchecked((byte)~(IccFlag | ExifFlag | XmpFlag));
            output.Write(chunk);
        }

        var stripped = output.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(stripped.AsSpan(4), (uint)(stripped.Length - 8));
        return stripped;
    }

    /// <summary>True when the image is VP8L (lossless) data, so re-encoding should stay lossless.</summary>
    public static bool IsLossless(byte[] webp) => Chunks(webp).Any(c => c.Id == "VP8L");

    // Each top-level chunk: its id, and where its whole chunk (header, payload and padding) sits.
    private static List<(string Id, int Start, int Length)> Chunks(byte[] webp)
    {
        if (webp.Length < 12 || !webp.AsSpan(0, 4).SequenceEqual("RIFF"u8) || !webp.AsSpan(8, 4).SequenceEqual("WEBP"u8))
            throw new InvalidDataException("Not a WebP file.");

        var declaredEnd = 8L + BinaryPrimitives.ReadUInt32LittleEndian(webp.AsSpan(4));
        if (declaredEnd > webp.Length) throw new InvalidDataException("The WebP file is cut short.");

        var chunks = new List<(string, int, int)>();
        var at = 12;
        while (at < declaredEnd)
        {
            if (at + 8 > declaredEnd) throw new InvalidDataException("The WebP file has a broken chunk.");
            var id = System.Text.Encoding.ASCII.GetString(webp, at, 4);
            var size = (long)BinaryPrimitives.ReadUInt32LittleEndian(webp.AsSpan(at + 4));
            if (at + 8 + size > declaredEnd) throw new InvalidDataException("The WebP file has a broken chunk.");
            // Odd-sized chunks are padded to an even length; tolerate a missing pad on the last one.
            var length = Math.Min(8 + size + (size & 1), declaredEnd - at);
            chunks.Add((id, at, (int)length));
            at += (int)length;
        }
        return chunks;
    }
}
