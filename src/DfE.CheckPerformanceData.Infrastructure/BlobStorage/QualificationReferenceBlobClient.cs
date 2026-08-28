using System.Text;
using System.Text.Json;
using Azure;
using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using DfE.CheckPerformanceData.Infrastructure.RulesEngine;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DfE.CheckPerformanceData.Infrastructure.BlobStorage;

/// <summary>
/// Reads the QualList qualification reference document from the rules-config container, and
/// self-seeds it from the bundled copy when absent — the same arrangement as
/// <see cref="GradeReferenceBlobClient"/>, and for the same reason: Terraform provisions an empty
/// container and the storage account has no public network access, so nothing outside the cluster
/// can upload the blob.
///
/// The document is shared by every school and every window and changes rarely, so it is cached for
/// five minutes. A missing blob caches as an empty lookup rather than throwing: the qualification
/// search page degrades to empty dropdowns instead of erroring, and a five-minute retry picks the
/// blob up once it lands.
/// </summary>
public sealed class QualificationReferenceBlobClient : IQualificationReferenceClient
{
    private const string CacheKey = "qualification-reference";
    private static readonly TimeSpan CacheExpiry = TimeSpan.FromMinutes(5);

    private readonly BlobServiceClient _blobServiceClient;
    private readonly IMemoryCache _cache;
    private readonly BlobRulesProviderOptions _options;
    private readonly ILogger<QualificationReferenceBlobClient> _logger;

    public QualificationReferenceBlobClient(
        BlobServiceClient blobServiceClient,
        IMemoryCache cache,
        IOptions<BlobRulesProviderOptions> options,
        ILogger<QualificationReferenceBlobClient> logger)
    {
        _blobServiceClient = blobServiceClient;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Public so tests bind fixture JSON exactly as production does.</summary>
    public static JsonSerializerOptions JsonOptions => ResultsEnquiryJson.Options;

    public async Task<QualificationReferenceLookup> GetLookupAsync(CancellationToken ct = default)
    {
        if (_cache.TryGetValue(CacheKey, out QualificationReferenceLookup? cached) && cached is not null)
            return cached;

        var lookup = await DownloadAsync(ct);
        _cache.Set(CacheKey, lookup, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheExpiry });
        return lookup;
    }

    /// <summary>
    /// Uploads the bundled document only if the blob is absent (If-None-Match=*), so a hand-edited
    /// or newer-than-bundled blob in a deployed environment is never clobbered. Best-effort: a
    /// storage failure is logged, not thrown, because the qualification search page degrading is far
    /// better than the whole app failing to start.
    /// </summary>
    public async Task SeedIfMissingAsync(string json, CancellationToken ct = default)
    {
        try
        {
            var container = _blobServiceClient.GetBlobContainerClient(_options.RulesBlobContainer);
            await container.CreateIfNotExistsAsync(cancellationToken: ct);

            var blob = container.GetBlobClient(ResultsEnquiryBlobPaths.QualificationReferenceBlobName);
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            await blob.UploadAsync(
                stream,
                new Azure.Storage.Blobs.Models.BlobUploadOptions
                {
                    Conditions = new Azure.Storage.Blobs.Models.BlobRequestConditions { IfNoneMatch = ETag.All }
                },
                ct);

            _cache.Remove(CacheKey);
            _logger.LogInformation(
                "Seeded qualification reference '{Blob}' into container '{Container}'.",
                ResultsEnquiryBlobPaths.QualificationReferenceBlobName, _options.RulesBlobContainer);
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            // Already present (or another instance won the race) — the whole point of the guard.
            _logger.LogInformation(
                "Qualification reference '{Blob}' already present; skipping seed.",
                ResultsEnquiryBlobPaths.QualificationReferenceBlobName);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to seed qualification reference '{Blob}'; the qualification search page will " +
                "have no options until the blob is provisioned.", ResultsEnquiryBlobPaths.QualificationReferenceBlobName);
        }
    }

    private async Task<QualificationReferenceLookup> DownloadAsync(CancellationToken ct)
    {
        var blob = _blobServiceClient
            .GetBlobContainerClient(_options.RulesBlobContainer)
            .GetBlobClient(ResultsEnquiryBlobPaths.QualificationReferenceBlobName);

        try
        {
            var response = await blob.DownloadContentAsync(ct);
            // Malformed JSON intentionally throws so a corrupt reference file surfaces rather than
            // silently emptying every qualification dropdown in the service.
            return QualificationReferenceLookup.Parse(response.Value.Content.ToString());
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogWarning(
                "Qualification reference '{Blob}' is not present in container '{Container}'; no " +
                "qualification will be selectable until it is seeded.",
                ResultsEnquiryBlobPaths.QualificationReferenceBlobName, _options.RulesBlobContainer);
            return QualificationReferenceLookup.Empty;
        }
    }
}
