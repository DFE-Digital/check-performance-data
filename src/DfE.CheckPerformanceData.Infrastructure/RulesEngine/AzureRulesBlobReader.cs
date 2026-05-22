using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace DfE.CheckPerformanceData.Infrastructure.RulesEngine;

/// <summary>
/// Real <see cref="IRulesBlobReader"/> backed by an Azure Blob container.
/// </summary>
public sealed class AzureRulesBlobReader : IRulesBlobReader
{
    private readonly BlobContainerClient _container;

    public AzureRulesBlobReader(BlobContainerClient container)
    {
        ArgumentNullException.ThrowIfNull(container);
        _container = container;
    }

    public async Task<RulesBlobFetch> FetchAsync(string blobName, string? ifNoneMatch, CancellationToken ct)
    {
        var client = _container.GetBlobClient(blobName);

        BlobDownloadResult result;
        try
        {
            var options = new BlobDownloadOptions();
            if (!string.IsNullOrEmpty(ifNoneMatch))
            {
                options.Conditions = new BlobRequestConditions { IfNoneMatch = new ETag(ifNoneMatch) };
            }
            var response = await client.DownloadContentAsync(options, ct).ConfigureAwait(false);
            result = response.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 304)
        {
            return RulesBlobFetch.NotModified(ifNoneMatch);
        }
        catch (RequestFailedException ex)
        {
            return RulesBlobFetch.Error($"Blob '{blobName}' fetch failed: {ex.Status} {ex.ErrorCode}.");
        }
        catch (Exception ex)
        {
            return RulesBlobFetch.Error($"Blob '{blobName}' fetch failed: {ex.GetType().Name} {ex.Message}.");
        }

        var content = result.Content?.ToString() ?? string.Empty;
        var etag = result.Details.ETag.ToString();
        return RulesBlobFetch.Success(content, etag);
    }
}
