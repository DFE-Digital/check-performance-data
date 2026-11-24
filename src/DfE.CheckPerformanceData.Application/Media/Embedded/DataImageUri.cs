using System.Text;
using DfE.CheckPerformanceData.Application.Media.Upload;

namespace DfE.CheckPerformanceData.Application.Media.Embedded;

/// <summary>
/// Decodes the <c>data:</c> URI of an image embedded in rich text: base64 (whitespace allowed) or
/// percent-encoded text, as an inline SVG often is. The size is checked before decoding, so a huge
/// URI is refused without allocating its bytes. It never throws: bad input gives a problem.
/// </summary>
public static class DataImageUri
{
    private const string Scheme = "data:";

    public static DecodedDataImage Decode(string? src)
    {
        var text = (src ?? "").Trim();
        if (!text.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase)) return Problem(EmbeddedImageProblems.NotImageData);

        var comma = text.IndexOf(',');
        var header = comma < 0 ? text[Scheme.Length..] : text[Scheme.Length..comma];
        var parts = header.Split(';', StringSplitOptions.TrimEntries);
        if (!parts[0].StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return Problem(EmbeddedImageProblems.NotImageData);
        if (comma < 0) return Problem(EmbeddedImageProblems.Unreadable);

        var payload = text[(comma + 1)..];
        var isBase64 = parts.Skip(1).Any(p => p.Equals("base64", StringComparison.OrdinalIgnoreCase));
        var bytes = isBase64 ? FromBase64(payload, out var problem) : FromPercentEncoded(payload, out problem);
        if (bytes is null) return Problem(problem!);
        if (bytes.Length == 0) return Problem(EmbeddedImageProblems.Empty);

        var format = MediaTypeSniffer.Sniff(bytes);
        return format is null ? Problem(EmbeddedImageProblems.NotSupported) : new DecodedDataImage(bytes, format, null);
    }

    private static byte[]? FromBase64(string payload, out string? problem)
    {
        problem = null;
        var clean = new StringBuilder(payload.Length);
        foreach (var c in payload)
        {
            if (!char.IsWhiteSpace(c)) clean.Append(c);
        }

        // Every 4 characters hold 3 bytes, so the size is known before decoding.
        if ((long)clean.Length / 4 * 3 > MediaUploadLimits.MaxFileBytes)
        {
            problem = EmbeddedImageProblems.TooLarge;
            return null;
        }

        // Padding is optional in some writers.
        if (clean.Length % 4 is 2 or 3) clean.Append('=', 4 - clean.Length % 4);
        var buffer = new byte[clean.Length / 4 * 3];
        if (!Convert.TryFromBase64String(clean.ToString(), buffer, out var written))
        {
            problem = EmbeddedImageProblems.Unreadable;
            return null;
        }

        return buffer[..written];
    }

    private static byte[]? FromPercentEncoded(string payload, out string? problem)
    {
        problem = null;
        // A percent-encoded byte takes up to 3 characters.
        if (payload.Length > MediaUploadLimits.MaxFileBytes * 3)
        {
            problem = EmbeddedImageProblems.TooLarge;
            return null;
        }

        var bytes = Encoding.UTF8.GetBytes(Uri.UnescapeDataString(payload));
        if (bytes.Length > MediaUploadLimits.MaxFileBytes)
        {
            problem = EmbeddedImageProblems.TooLarge;
            return null;
        }

        return bytes;
    }

    private static DecodedDataImage Problem(string problem) => new(null, null, problem);
}
