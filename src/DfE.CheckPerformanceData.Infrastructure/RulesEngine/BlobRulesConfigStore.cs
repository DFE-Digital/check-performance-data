using System.Text;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using DfE.CheckPerformanceData.Application.RulesConfig;
using Microsoft.Extensions.Options;

namespace DfE.CheckPerformanceData.Infrastructure.RulesEngine;

/// <summary>
/// Read/write access to the rules-config blobs for the admin editor. Reuses the same
/// container/blob names as <see cref="BlobRulesProvider"/> (via BlobRulesProviderOptions) so
/// the worker reads exactly what the editor writes. Writes use an ETag condition for optimistic
/// concurrency; pass null to create a not-yet-existing blob.
/// </summary>
public sealed class BlobRulesConfigStore : IRulesConfigStore
{
    private readonly BlobContainerClient _container;
    private readonly BlobRulesProviderOptions _options;

    public BlobRulesConfigStore(BlobServiceClient service, IOptions<BlobRulesProviderOptions> options)
    {
        ArgumentNullException.ThrowIfNull(service);
        _options = options.Value ?? throw new ArgumentNullException(nameof(options));
        _container = service.GetBlobContainerClient(_options.RulesBlobContainer);
    }

    private string BlobName(RulesConfigType type) =>
        type == RulesConfigType.Rules ? _options.RulesBlobName : _options.LookupsBlobName;

    public async Task<RulesConfigBlob> ReadAsync(RulesConfigType type, CancellationToken ct = default)
    {
        var client = _container.GetBlobClient(BlobName(type));
        var response = await client.DownloadContentAsync(ct).ConfigureAwait(false);
        var content = response.Value.Content?.ToString() ?? string.Empty;
        var etag = response.Value.Details.ETag.ToString();
        return new RulesConfigBlob(content, etag);
    }

    public async Task WriteAsync(RulesConfigType type, string content, string? expectedETag, CancellationToken ct = default)
    {
        await _container.CreateIfNotExistsAsync(cancellationToken: ct).ConfigureAwait(false);
        var client = _container.GetBlobClient(BlobName(type));

        var conditions = string.IsNullOrEmpty(expectedETag)
            ? new BlobRequestConditions { IfNoneMatch = ETag.All }
            : new BlobRequestConditions { IfMatch = new ETag(expectedETag) };

        var uploadOptions = new BlobUploadOptions
        {
            Conditions = conditions,
            HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" }
        };

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        try
        {
            await client.UploadAsync(stream, uploadOptions, ct).ConfigureAwait(false);
        }
        catch (RequestFailedException ex) when (ex.Status == 412 || ex.Status == 409)
        {
            throw new RulesConfigConflictException(
                $"The {type} config was changed by someone else since it was loaded. Reload and re-apply your changes.");
        }
    }
}
