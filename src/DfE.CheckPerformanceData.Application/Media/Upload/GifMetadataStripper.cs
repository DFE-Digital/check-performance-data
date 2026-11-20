namespace DfE.CheckPerformanceData.Application.Media.Upload;

/// <summary>
/// Removes the parts of a GIF that are not the picture: comments, plain-text blocks, application
/// blocks other than the NETSCAPE/ANIMEXTS loop count, and anything after the trailer (where a
/// polyglot hides its payload). Frames and their timing are copied unchanged. A file whose block
/// structure is broken or cut short throws <see cref="InvalidDataException"/>.
/// </summary>
public static class GifMetadataStripper
{
    private const byte ExtensionIntroducer = 0x21;
    private const byte ImageSeparator = 0x2C;
    private const byte Trailer = 0x3B;
    private const byte GraphicControlLabel = 0xF9;
    private const byte ApplicationLabel = 0xFF;

    public static byte[] Strip(byte[] gif)
    {
        var reader = new Reader(gif);
        using var output = new MemoryStream(gif.Length);

        var header = reader.Take(13);
        if (!header[..6].SequenceEqual("GIF87a"u8) && !header[..6].SequenceEqual("GIF89a"u8))
            throw new InvalidDataException("Not a GIF.");
        output.Write(header);
        if ((header[10] & 0x80) != 0) output.Write(reader.Take(ColourTableSize(header[10])));

        while (true)
        {
            var start = reader.Position;
            switch (reader.Byte())
            {
                case ImageSeparator:
                    var descriptor = reader.Take(9);
                    if ((descriptor[8] & 0x80) != 0) reader.Take(ColourTableSize(descriptor[8]));
                    reader.Byte(); // LZW minimum code size
                    reader.SkipSubBlocks();
                    output.Write(gif, start, reader.Position - start);
                    break;

                case ExtensionIntroducer:
                    var label = reader.Byte();
                    var keep = label == GraphicControlLabel || (label == ApplicationLabel && IsLoopBlock(reader));
                    reader.SkipSubBlocks();
                    if (keep) output.Write(gif, start, reader.Position - start);
                    break;

                case Trailer:
                    output.WriteByte(Trailer);
                    return output.ToArray();

                default:
                    throw new InvalidDataException("Unknown GIF block.");
            }
        }
    }

    private static int ColourTableSize(byte packed) => 3 * (1 << ((packed & 0x07) + 1));

    // The application identifier is the first sub-block: 11 bytes, read here without consuming it.
    private static bool IsLoopBlock(Reader reader)
    {
        var block = reader.Peek(12);
        return block[0] == 11 && (block[1..].SequenceEqual("NETSCAPE2.0"u8) || block[1..].SequenceEqual("ANIMEXTS1.0"u8));
    }

    private sealed class Reader(byte[] bytes)
    {
        public int Position { get; private set; }

        public byte Byte() => Take(1)[0];

        public ReadOnlySpan<byte> Take(int count)
        {
            var span = Peek(count);
            Position += count;
            return span;
        }

        public ReadOnlySpan<byte> Peek(int count)
        {
            if (count < 0 || Position + count > bytes.Length) throw new InvalidDataException("The GIF is cut short.");
            return bytes.AsSpan(Position, count);
        }

        public void SkipSubBlocks()
        {
            int size;
            while ((size = Byte()) != 0) Take(size);
        }
    }
}
