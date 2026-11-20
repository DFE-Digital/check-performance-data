using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using DfE.CheckPerformanceData.Application.Media;

namespace DfE.CheckPerformanceData.Infrastructure.BlobStorage;

/// <summary>
/// Media library files in the app storage account's cms-media container. Terraform provisions the
/// container in deployed environments; a write creates it when missing, as the other blob clients
/// do, so a fresh local stack works with no set-up. Reads never create it. Writes are create-only
/// (If-None-Match: *), so a stored original is never replaced.
/// </summary>
public sealed class MediaBlobStore(BlobServiceClient service, string containerName = MediaBlobKeys.ContainerName) : IMediaBlobStore
{
    private BlobContainerClient Container => service.GetBlobContainerClient(containerName);

    public async Task<bool> PutAsync(string key, Stream content, string contentType, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        await Container.CreateIfNotExistsAsync(cancellationToken: ct);
        try
        {
            await Container.GetBlobClient(key).UploadAsync(content, new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
                Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All }
            }, ct);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status is 409 or 412)
        {
            return false;
        }
    }

    public async Task<MediaBlob?> GetAsync(string key, CancellationToken ct = default)
    {
        try
        {
            var result = await Container.GetBlobClient(key).DownloadStreamingAsync(cancellationToken: ct);
            var details = result.Value.Details;
            return new MediaBlob(result.Value.Content, details.ContentType, details.ContentLength);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        try
        {
            return (await Container.GetBlobClient(key).ExistsAsync(ct)).Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return false;
        }
    }

    public async Task<bool> DeleteAsync(string key, CancellationToken ct = default)
    {
        try
        {
            return (await Container.GetBlobClient(key).DeleteIfExistsAsync(cancellationToken: ct)).Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return false;
        }
    }
}
