namespace DfE.CheckPerformanceData.Application.Media.Upload;

/// <summary>
/// Reads an upload into memory, stopping one byte past the size limit so an oversized or endless
/// stream is refused without being read in full. Then decides the type from the bytes and sends
/// the file to the SVG sanitiser or the raster cleaner.
/// </summary>
public sealed class MediaFileCleaner(IRasterImageCleaner raster) : IMediaFileCleaner
{
    public async Task<MediaCheckResult> CleanAsync(Stream content, CancellationToken ct = default)
    {
        var bytes = await ReadUpToLimitAsync(content, ct);
        if (bytes is null) return MediaCheckResult.Reject(MediaRejections.TooLarge);
        if (bytes.Length == 0) return MediaCheckResult.Reject(MediaRejections.Empty);

        return MediaTypeSniffer.Sniff(bytes) switch
        {
            null => MediaCheckResult.Reject(MediaRejections.NotAnImage),
            MediaFormat.Svg => SvgSanitiser.Sanitise(bytes),
            var format => raster.Clean(bytes, format.Value)
        };
    }

    // Null when the stream holds more than the limit.
    private static async Task<byte[]?> ReadUpToLimitAsync(Stream content, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await content.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > MediaUploadLimits.MaxFileBytes) return null;
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }
}
