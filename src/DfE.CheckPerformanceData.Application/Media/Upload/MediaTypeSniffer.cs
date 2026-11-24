namespace DfE.CheckPerformanceData.Application.Media.Upload;

/// <summary>
/// Decides a file's format from its first bytes. The file name and the content type the browser
/// claimed are never used. Markup only makes a file an SVG candidate: the SVG sanitiser still has
/// to parse it and find an svg root, so HTML or any other XML is refused there.
/// </summary>
public static class MediaTypeSniffer
{
    public static MediaFormat? Sniff(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])) return MediaFormat.Png;
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF])) return MediaFormat.Jpeg;
        if (bytes.StartsWith("GIF87a"u8) || bytes.StartsWith("GIF89a"u8)) return MediaFormat.Gif;
        if (bytes.Length >= 12 && bytes.StartsWith("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8)) return MediaFormat.WebP;
        return StartsWithMarkup(bytes) ? MediaFormat.Svg : null;
    }

    private static bool StartsWithMarkup(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF])) bytes = bytes[3..];
        foreach (var b in bytes)
        {
            if (b is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n') continue;
            return b == (byte)'<';
        }
        return false;
    }
}
