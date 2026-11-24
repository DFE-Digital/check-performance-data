using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using SkiaSharp;

namespace DfE.CheckPerformanceData.Application.UnitTests.Media.Upload;

// Builds the image files the upload tests need, in code, so every fixture says what is in it.
// Skia encodes the plain PNG, JPEG and WebP images; EXIF, XMP, animation and hostile headers are
// written by hand, because Skia writes none of them.
internal static class ImageFixtures
{
    public static readonly SKColor Red = new(255, 0, 0);
    public static readonly SKColor Blue = new(0, 0, 255);

    public static SKBitmap Bitmap(int width, int height, Func<int, int, SKColor> colourAt)
    {
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                bitmap.SetPixel(x, y, colourAt(x, y));
        return bitmap;
    }

    public static byte[] Encode(SKBitmap bitmap, SKEncodedImageFormat format, int quality = 95)
    {
        using var data = bitmap.Encode(format, quality);
        return data.ToArray();
    }

    public static byte[] Png(int width = 8, int height = 6) =>
        Encode(Bitmap(width, height, (_, _) => Red), SKEncodedImageFormat.Png);

    public static byte[] Jpeg(int width = 8, int height = 6) =>
        Encode(Bitmap(width, height, (_, _) => Red), SKEncodedImageFormat.Jpeg);

    // Left half red, right half blue: after an orientation is applied, where each colour lands
    // shows which way the picture turned.
    public static byte[] HalfAndHalfJpeg(int width, int height) =>
        Encode(Bitmap(width, height, (x, _) => x < width / 2 ? Red : Blue), SKEncodedImageFormat.Jpeg);

    public static byte[] WebPLossless(int width = 4, int height = 4, SKColor? colour = null)
    {
        using var bitmap = Bitmap(width, height, (_, _) => colour ?? Red);
        using var data = bitmap.PeekPixels().Encode(new SKWebpEncoderOptions(SKWebpEncoderCompression.Lossless, 100));
        return data!.ToArray();
    }

    public static byte[] WebPLossy(int width = 8, int height = 8) =>
        Encode(Bitmap(width, height, (_, _) => Red), SKEncodedImageFormat.Webp, 80);

    // ---- JPEG metadata ----------------------------------------------------------------------

    /// <summary>Puts APP segments straight after the JPEG's start-of-image marker.</summary>
    public static byte[] WithSegments(byte[] jpeg, params byte[][] segments)
    {
        var output = new List<byte>(jpeg[..2]);
        foreach (var segment in segments) output.AddRange(segment);
        output.AddRange(jpeg[2..]);
        return output.ToArray();
    }

    private static byte[] Segment(byte marker, byte[] payload)
    {
        var segment = new byte[4 + payload.Length];
        segment[0] = 0xFF;
        segment[1] = marker;
        BinaryPrimitives.WriteUInt16BigEndian(segment.AsSpan(2), (ushort)(payload.Length + 2));
        payload.CopyTo(segment, 4);
        return segment;
    }

    /// <summary>
    /// An EXIF APP1 segment (big-endian TIFF) with an orientation, an image description and a GPS
    /// block holding a latitude reference and a GPS area text, so a test can look for the text.
    /// </summary>
    public static byte[] ExifSegment(ushort orientation, string description, string gpsArea)
    {
        var descriptionBytes = Encoding.ASCII.GetBytes(description + "\0");
        var gpsAreaBytes = Encoding.ASCII.GetBytes("ASCII\0\0\0" + gpsArea);

        // TIFF layout: header (8), IFD0 with 3 entries (2 + 36 + 4), GPS IFD with 2 entries
        // (2 + 24 + 4), then the description and GPS area values.
        const int ifd0 = 8;
        const int gpsIfd = ifd0 + 2 + 3 * 12 + 4;
        const int values = gpsIfd + 2 + 2 * 12 + 4;
        var descriptionAt = values;
        var gpsAreaAt = descriptionAt + descriptionBytes.Length;

        var tiff = new byte[gpsAreaAt + gpsAreaBytes.Length];
        var span = tiff.AsSpan();
        "MM"u8.CopyTo(span);
        BinaryPrimitives.WriteUInt16BigEndian(span[2..], 42);
        BinaryPrimitives.WriteUInt32BigEndian(span[4..], ifd0);

        BinaryPrimitives.WriteUInt16BigEndian(span[ifd0..], 3);
        Entry(span, ifd0 + 2, 0x010E, 2, (uint)descriptionBytes.Length, (uint)descriptionAt);
        Entry(span, ifd0 + 14, 0x0112, 3, 1, (uint)orientation << 16);
        Entry(span, ifd0 + 26, 0x8825, 4, 1, gpsIfd);

        BinaryPrimitives.WriteUInt16BigEndian(span[gpsIfd..], 2);
        Entry(span, gpsIfd + 2, 0x0001, 2, 2, (uint)'N' << 24);
        Entry(span, gpsIfd + 14, 0x001C, 7, (uint)gpsAreaBytes.Length, (uint)gpsAreaAt);

        descriptionBytes.CopyTo(span[descriptionAt..]);
        gpsAreaBytes.CopyTo(span[gpsAreaAt..]);

        return Segment(0xE1, [.. "Exif\0\0"u8.ToArray(), .. tiff]);
    }

    private static void Entry(Span<byte> span, int at, ushort tag, ushort type, uint count, uint value)
    {
        BinaryPrimitives.WriteUInt16BigEndian(span[at..], tag);
        BinaryPrimitives.WriteUInt16BigEndian(span[(at + 2)..], type);
        BinaryPrimitives.WriteUInt32BigEndian(span[(at + 4)..], count);
        BinaryPrimitives.WriteUInt32BigEndian(span[(at + 8)..], value);
    }

    public static byte[] XmpSegment(string text) =>
        Segment(0xE1, Encoding.ASCII.GetBytes("http://ns.adobe.com/xap/1.0/\0<x:xmpmeta>" + text + "</x:xmpmeta>"));

    public static byte[] CommentSegment(string text) => Segment(0xFE, Encoding.ASCII.GetBytes(text));

    // ---- PNG header that claims a huge canvas ---------------------------------------------

    /// <summary>
    /// A PNG of a few dozen bytes whose header claims the given size. Decoding it would need
    /// width x height x 4 bytes, so it must be refused from the header alone.
    /// </summary>
    public static byte[] PngClaiming(int width, int height)
    {
        var output = new List<byte>(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), height);
        ihdr[8] = 8; // bit depth
        ihdr[9] = 6; // RGBA
        output.AddRange(PngChunk("IHDR", ihdr));
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
            zlib.Write(new byte[64]);
        output.AddRange(PngChunk("IDAT", compressed.ToArray()));
        output.AddRange(PngChunk("IEND", []));
        return output.ToArray();
    }

    private static byte[] PngChunk(string type, byte[] data)
    {
        var chunk = new byte[12 + data.Length];
        BinaryPrimitives.WriteInt32BigEndian(chunk, data.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(chunk, 4);
        data.CopyTo(chunk, 8);
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(8 + data.Length), Crc32(chunk.AsSpan(4, 4 + data.Length)));
        return chunk;
    }

    private static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in bytes)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
                crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
        }
        return ~crc;
    }

    // ---- GIF -------------------------------------------------------------------------------

    /// <summary>
    /// A GIF89a of the given size with one frame per entry in <paramref name="frames"/> (each a
    /// palette index, 0 = red, 1 = blue, filling the frame). Optional blocks: the NETSCAPE loop,
    /// a comment, an XMP application block, a plain-text block and bytes after the trailer.
    /// </summary>
    public static byte[] Gif(
        int width,
        int height,
        int[] frames,
        bool loop = true,
        string? comment = null,
        string? xmp = null,
        string? plainText = null,
        string? afterTrailer = null)
    {
        var gif = new List<byte>();
        gif.AddRange("GIF89a"u8.ToArray());
        gif.AddRange(LittleEndian16(width));
        gif.AddRange(LittleEndian16(height));
        gif.Add(0x80); // global colour table, 2 entries
        gif.Add(0);
        gif.Add(0);
        gif.AddRange(new byte[] { 255, 0, 0, 0, 0, 255 });

        if (loop)
        {
            gif.AddRange(new byte[] { 0x21, 0xFF, 11 });
            gif.AddRange("NETSCAPE2.0"u8.ToArray());
            gif.AddRange(new byte[] { 3, 1, 0, 0, 0 });
        }
        if (comment is not null)
        {
            gif.AddRange(new byte[] { 0x21, 0xFE });
            gif.AddRange(SubBlocks(Encoding.ASCII.GetBytes(comment)));
        }
        if (xmp is not null)
        {
            gif.AddRange(new byte[] { 0x21, 0xFF, 11 });
            gif.AddRange("XMP DataXMP"u8.ToArray());
            gif.AddRange(SubBlocks(Encoding.ASCII.GetBytes(xmp)));
        }
        if (plainText is not null)
        {
            gif.AddRange(new byte[] { 0x21, 0x01, 12, 0, 0, 0, 0, 1, 0, 1, 0, 1, 1, 0, 1 });
            gif.AddRange(SubBlocks(Encoding.ASCII.GetBytes(plainText)));
        }

        foreach (var index in frames)
        {
            gif.AddRange(new byte[] { 0x21, 0xF9, 4, 0x04, 10, 0, 0, 0 }); // 0.1 s, keep previous
            gif.Add(0x2C);
            gif.AddRange(LittleEndian16(0));
            gif.AddRange(LittleEndian16(0));
            gif.AddRange(LittleEndian16(width));
            gif.AddRange(LittleEndian16(height));
            gif.Add(0);
            gif.Add(2); // LZW minimum code size
            gif.AddRange(SubBlocks(Lzw(index, width * height)));
        }

        gif.Add(0x3B);
        if (afterTrailer is not null) gif.AddRange(Encoding.ASCII.GetBytes(afterTrailer));
        return gif.ToArray();
    }

    private static byte[] LittleEndian16(int value) => [(byte)value, (byte)(value >> 8)];

    private static byte[] SubBlocks(byte[] data)
    {
        var output = new List<byte>();
        for (var i = 0; i < data.Length; i += 255)
        {
            var length = Math.Min(255, data.Length - i);
            output.Add((byte)length);
            output.AddRange(data.AsSpan(i, length).ToArray());
        }
        output.Add(0);
        return output.ToArray();
    }

    // Uncompressed LZW with a 2-bit minimum code size: a clear code before every two literal
    // codes keeps the code width at 3 bits, which every decoder reads the same way.
    private static byte[] Lzw(int index, int pixels)
    {
        const int clear = 4, end = 5, width = 3;
        var codes = new List<int>();
        for (var i = 0; i < pixels; i++)
        {
            if (i % 2 == 0) codes.Add(clear);
            codes.Add(index);
        }
        codes.Add(end);

        var output = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var code in codes)
        {
            buffer |= code << bits;
            bits += width;
            while (bits >= 8)
            {
                output.Add((byte)buffer);
                buffer >>= 8;
                bits -= 8;
            }
        }
        if (bits > 0) output.Add((byte)buffer);
        return output.ToArray();
    }

    // ---- WebP ------------------------------------------------------------------------------

    public const byte VP8XAnimation = 0x02;
    public const byte VP8XXmp = 0x04;
    public const byte VP8XExif = 0x08;
    public const byte VP8XIcc = 0x20;

    /// <summary>
    /// An animated WebP: one ANMF frame per colour, each holding the VP8L chunk Skia wrote for a
    /// solid lossless image, plus optional EXIF, XMP and ICCP chunks and bytes after the RIFF.
    /// </summary>
    public static byte[] AnimatedWebP(
        int width,
        int height,
        SKColor[] frames,
        string? exif = null,
        string? xmp = null,
        string? icc = null,
        string? afterRiff = null)
    {
        byte flags = VP8XAnimation;
        if (exif is not null) flags |= VP8XExif;
        if (xmp is not null) flags |= VP8XXmp;
        if (icc is not null) flags |= VP8XIcc;

        var body = new List<byte>("WEBP"u8.ToArray());
        var vp8x = new byte[10];
        vp8x[0] = flags;
        WriteUInt24(vp8x, 4, width - 1);
        WriteUInt24(vp8x, 7, height - 1);
        body.AddRange(RiffChunk("VP8X", vp8x));
        if (icc is not null) body.AddRange(RiffChunk("ICCP", Encoding.ASCII.GetBytes(icc)));
        body.AddRange(RiffChunk("ANIM", [0, 0, 0, 0, 0, 0]));

        foreach (var colour in frames)
        {
            var still = WebPLossless(width, height, colour);
            var vp8l = TopLevelChunk(still, "VP8L");
            var anmf = new List<byte>();
            var header = new byte[16];
            WriteUInt24(header, 6, width - 1);
            WriteUInt24(header, 9, height - 1);
            WriteUInt24(header, 12, 100);
            anmf.AddRange(header);
            anmf.AddRange(RiffChunk("VP8L", vp8l));
            body.AddRange(RiffChunk("ANMF", anmf.ToArray()));
        }

        if (exif is not null) body.AddRange(RiffChunk("EXIF", Encoding.ASCII.GetBytes(exif)));
        if (xmp is not null) body.AddRange(RiffChunk("XMP ", Encoding.ASCII.GetBytes(xmp)));

        var riff = new List<byte>("RIFF"u8.ToArray());
        var size = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(size, (uint)body.Count);
        riff.AddRange(size);
        riff.AddRange(body);
        if (afterRiff is not null) riff.AddRange(Encoding.ASCII.GetBytes(afterRiff));
        return riff.ToArray();
    }

    /// <summary>The payload of the first top-level chunk with this id in a RIFF WebP.</summary>
    public static byte[] TopLevelChunk(byte[] webp, string id)
    {
        var at = 12;
        while (at + 8 <= webp.Length)
        {
            var chunkId = Encoding.ASCII.GetString(webp, at, 4);
            var size = (int)BinaryPrimitives.ReadUInt32LittleEndian(webp.AsSpan(at + 4));
            if (chunkId == id) return webp.AsSpan(at + 8, size).ToArray();
            at += 8 + size + (size & 1);
        }
        throw new InvalidOperationException($"No {id} chunk.");
    }

    public static IReadOnlyList<string> TopLevelChunkIds(byte[] webp)
    {
        var ids = new List<string>();
        var at = 12;
        while (at + 8 <= webp.Length)
        {
            ids.Add(Encoding.ASCII.GetString(webp, at, 4));
            var size = (int)BinaryPrimitives.ReadUInt32LittleEndian(webp.AsSpan(at + 4));
            at += 8 + size + (size & 1);
        }
        return ids;
    }

    private static byte[] RiffChunk(string id, byte[] payload)
    {
        var chunk = new byte[8 + payload.Length + (payload.Length & 1)];
        Encoding.ASCII.GetBytes(id).CopyTo(chunk, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(4), (uint)payload.Length);
        payload.CopyTo(chunk, 8);
        return chunk;
    }

    private static void WriteUInt24(byte[] buffer, int at, int value)
    {
        buffer[at] = (byte)value;
        buffer[at + 1] = (byte)(value >> 8);
        buffer[at + 2] = (byte)(value >> 16);
    }

    // ---- helpers for assertions ------------------------------------------------------------

    public static bool Contains(byte[] haystack, string text) =>
        haystack.AsSpan().IndexOf(Encoding.ASCII.GetBytes(text)) >= 0;

    public static string Text(byte[] bytes) => Encoding.UTF8.GetString(bytes);
}
