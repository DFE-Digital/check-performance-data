using Microsoft.Extensions.Logging;

namespace DfE.CheckPerformanceData.Application.Media.Serving;

/// <summary>
/// Serves media files and their renditions.
///
/// The query is read and its signature checked before anything is looked up, so an unsigned or
/// tampered URL costs no database or storage read. The version record decides the content type
/// and whether the file can be resized: an SVG, an animation, or an image of unknown size is
/// always served as stored, whatever the URL asks for.
///
/// A rendition is made once and kept in blob storage under a name made from its settings. The
/// first request renders it inside <see cref="MediaProcessingGate"/>, then looks in storage again
/// in case a request that waited at the gate before it already stored one. Two requests can
/// still render the same copy at the same time; stored files are never overwritten, so the second
/// write does nothing and that request sends the copy it made. Both copies are identical.
/// </summary>
public sealed class MediaDeliveryService(
    IMediaAssetRepository assets,
    IMediaBlobStore blobs,
    IMediaRenditionRenderer renderer,
    MediaProcessingGate gate,
    MediaUrlSigner signer,
    ILogger<MediaDeliveryService> logger) : IMediaDeliveryService
{
    private const string RenditionContentType = "image/webp";

    public async Task<MediaDelivery> GetAsync(Guid assetId, int version, IEnumerable<KeyValuePair<string, string?>> query, CancellationToken ct = default)
    {
        var parsed = MediaRenditionQuery.Parse(query);
        if (!parsed.IsValid) return MediaDelivery.Invalid;
        var rendition = parsed.Rendition;
        if (rendition is not null && !signer.IsValid(assetId, version, rendition, parsed.Signature))
        {
            logger.LogInformation("Media rendition refused for {AssetId} version {Version}: the signature does not match", assetId, version);
            return MediaDelivery.Invalid;
        }

        var record = await assets.GetVersionAsync(assetId, version, ct);
        if (record is null) return MediaDelivery.NotFound;

        return rendition is not null && MediaUrlBuilder.IsResizable(record)
            ? await RenditionAsync(record, rendition, ct)
            : await OriginalAsync(record, ct);
    }

    private async Task<MediaDelivery> OriginalAsync(MediaAssetVersionDto record, CancellationToken ct)
    {
        var blob = await blobs.GetAsync(record.BlobKey, ct);
        if (blob is null)
        {
            logger.LogWarning("Media file {BlobKey} is missing for {AssetId} version {Version}", record.BlobKey, record.AssetId, record.Version);
            return MediaDelivery.NotFound;
        }
        return new MediaDelivery(MediaDeliveryOutcome.Found, blob.Content, record.ContentType, blob.Length, ETag(record, null));
    }

    private async Task<MediaDelivery> RenditionAsync(MediaAssetVersionDto record, MediaRendition rendition, CancellationToken ct)
    {
        var hash = MediaRenditionQuery.Hash(rendition);
        var key = MediaBlobKeys.Rendition(record.AssetId, record.Version, hash);
        var etag = ETag(record, hash);

        var stored = await blobs.GetAsync(key, ct);
        if (stored is not null) return Found(stored.Content, stored.Length, etag);

        byte[] rendered;
        using (await gate.EnterAsync(ct))
        {
            stored = await blobs.GetAsync(key, ct);
            if (stored is not null) return Found(stored.Content, stored.Length, etag);

            var source = await ReadAsync(record.BlobKey, ct);
            if (source is null)
            {
                logger.LogWarning("Media file {BlobKey} is missing for {AssetId} version {Version}", record.BlobKey, record.AssetId, record.Version);
                return MediaDelivery.NotFound;
            }
            var layout = MediaRenditionGeometry.Compute(record.Width!.Value, record.Height!.Value, rendition);
            rendered = renderer.Render(source, layout);
        }

        if (await blobs.PutAsync(key, new MemoryStream(rendered, writable: false), RenditionContentType, ct))
            logger.LogInformation("Media rendition {BlobKey} stored ({SizeBytes} bytes)", key, rendered.Length);
        return Found(new MemoryStream(rendered, writable: false), rendered.Length, etag);
    }

    private static MediaDelivery Found(Stream content, long length, string etag) =>
        new(MediaDeliveryOutcome.Found, content, RenditionContentType, length, etag);

    private async Task<byte[]?> ReadAsync(string key, CancellationToken ct)
    {
        await using var blob = await blobs.GetAsync(key, ct);
        if (blob is null) return null;
        using var copy = new MemoryStream();
        await blob.Content.CopyToAsync(copy, ct);
        return copy.ToArray();
    }

    private static string ETag(MediaAssetVersionDto record, string? renditionHash) =>
        renditionHash is null ? $"\"{record.AssetId:N}.{record.Version}\"" : $"\"{record.AssetId:N}.{record.Version}.{renditionHash}\"";
}
