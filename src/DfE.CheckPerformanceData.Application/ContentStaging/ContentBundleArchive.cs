using System.IO.Compression;
using System.Text;

namespace DfE.CheckPerformanceData.Application.ContentStaging;

/// <summary>Raised when an uploaded bundle is not a shape this can read.</summary>
public sealed class ContentBundleFormatException(string message) : Exception(message);

/// <summary>
/// Puts a bundle on the wire as a single-entry zip, and reads one back.
///
/// Bundles are JSON carrying a lot of repeated structure, which deflate handles well — an
/// order-of-magnitude reduction in transfer size for a few lines of code. Nothing about the
/// import semantics changes; the file is just smaller in transit.
///
/// Reading sniffs the leading bytes rather than trusting the extension, so the plain .json
/// bundles already in circulation keep importing, and a zip renamed to .json still works.
/// </summary>
public static class ContentBundleArchive
{
    // The single entry inside an exported zip.
    public const string EntryName = "bundle.json";

    // Every zip signature starts "PK" — \x03\x04 for a local file header, \x05\x06 for an
    // archive with no entries, \x07\x08 for a spanned one. Sniffing the two-byte prefix rather
    // than the full local-header signature means an empty or unusual archive is still recognised
    // as a zip and reported as a broken one, instead of falling through to be read as JSON.
    // Nothing is lost by the wider net: valid JSON starts with '{', '[' or whitespace.
    private static ReadOnlySpan<byte> ZipMagic => [0x50, 0x4B];
    private static ReadOnlySpan<byte> Utf8Bom => [0xEF, 0xBB, 0xBF];

    // Rejects invalid byte sequences rather than silently substituting U+FFFD, which would
    // corrupt content invisibly and could land malformed strings in the database.
    private static readonly UTF8Encoding StrictUtf8 =
        new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static byte[] ToZip(string bundleJson)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var entry = archive.CreateEntry(EntryName, CompressionLevel.Optimal).Open();
            entry.Write(StrictUtf8.GetBytes(bundleJson));
        }
        return buffer.ToArray();
    }

    public static bool LooksLikeZip(ReadOnlySpan<byte> content) =>
        content.Length >= ZipMagic.Length && content[..ZipMagic.Length].SequenceEqual(ZipMagic);

    /// <summary>
    /// Returns the bundle JSON from an uploaded file, whether it arrived zipped or plain.
    /// </summary>
    /// <exception cref="ContentBundleFormatException">The zip is corrupt, empty, or oversized.</exception>
    /// <exception cref="DecoderFallbackException">The content is not valid UTF-8.</exception>
    public static string ReadBundleJson(byte[] content, long maxDecompressedBytes)
    {
        var bytes = LooksLikeZip(content) ? Inflate(content, maxDecompressedBytes) : content;
        return StrictUtf8.GetString(StripBom(bytes));
    }

    private static ReadOnlySpan<byte> StripBom(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= Utf8Bom.Length && bytes[..Utf8Bom.Length].SequenceEqual(Utf8Bom)
            ? bytes[Utf8Bom.Length..]
            : bytes;

    private static byte[] Inflate(byte[] zipped, long maxDecompressedBytes)
    {
        ZipArchive archive;
        try
        {
            archive = new ZipArchive(new MemoryStream(zipped), ZipArchiveMode.Read);
        }
        catch (InvalidDataException ex)
        {
            throw new ContentBundleFormatException(
                $"The bundle file is not a readable zip archive ({ex.Message}).");
        }

        using (archive)
        {
            // An export is defined as a single entry named exactly bundle.json at the root, so
            // that is matched on FullName and taken first. Matching on Name instead would match
            // the leaf of any path, and a nested payload/bundle.json sorted earlier would then
            // be imported in preference to the bundle.json an operator sees when they open the
            // archive — what gets imported has to be what the file appears to contain.
            //
            // The looser searches exist only for archives we did not write: a bundle re-zipped
            // by hand, or one round-tripped through macOS, which picks up __MACOSX resource
            // forks that would otherwise shadow the real entry.
            var entry =
                archive.Entries.FirstOrDefault(e =>
                    e.FullName.Equals(EntryName, StringComparison.OrdinalIgnoreCase))
                ?? archive.Entries.FirstOrDefault(e =>
                    e.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                    && !e.FullName.StartsWith("__MACOSX/", StringComparison.Ordinal))
                ?? archive.Entries.FirstOrDefault(e => e.Length > 0);

            if (entry is null)
            {
                throw new ContentBundleFormatException("The bundle archive is empty.");
            }

            try
            {
                using var stream = entry.Open();
                return ReadBounded(stream, maxDecompressedBytes);
            }
            catch (InvalidDataException ex)
            {
                throw new ContentBundleFormatException(
                    $"The bundle archive could not be decompressed ({ex.Message}).");
            }
        }
    }

    // Enforces the ceiling as the bytes arrive rather than trusting ZipArchiveEntry.Length,
    // which is read from the archive's own header and is therefore attacker-controlled. A few
    // kilobytes of zip can otherwise declare a modest size and expand to gigabytes.
    private static byte[] ReadBounded(Stream stream, long maxBytes)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                throw new ContentBundleFormatException(
                    $"The bundle is too large once decompressed. The limit is {maxBytes / (1024 * 1024)} MB.");
            }
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }
}
