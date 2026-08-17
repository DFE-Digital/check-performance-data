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
/// Reads the AODC grade reference document from the rules-config container, and self-seeds it from
/// the bundled copy when absent — the same arrangement <see cref="RulesConfigSeeder"/> uses, and for
/// the same reason: Terraform provisions an empty container and the storage account has no public
/// network access, so nothing outside the cluster can upload the blob.
///
/// The document is shared by every school and every window and changes at most once a year, so it is
/// cached for five minutes. A missing blob caches as an empty lookup rather than throwing: the
/// details page degrades to "we cannot list grades for this qualification yet" instead of erroring,
/// and a five-minute retry picks the blob up once it lands.
/// </summary>
public sealed class GradeReferenceBlobClient : IGradeReferenceClient
{
    private const string CacheKey = "grade-reference";
    private static readonly TimeSpan CacheExpiry = TimeSpan.FromMinutes(5);

    private readonly BlobServiceClient _blobServiceClient;
    private readonly IMemoryCache _cache;
    private readonly BlobRulesProviderOptions _options;
    private readonly ILogger<GradeReferenceBlobClient> _logger;

    public GradeReferenceBlobClient(
        BlobServiceClient blobServiceClient,
        IMemoryCache cache,
        IOptions<BlobRulesProviderOptions> options,
        ILogger<GradeReferenceBlobClient> logger)
    {
        _blobServiceClient = blobServiceClient;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Public so tests bind fixture JSON exactly as production does.</summary>
    public static JsonSerializerOptions JsonOptions => ResultsEnquiryJson.Options;

    public async Task<GradeReference?> GetByQanAsync(string qan, CancellationToken ct = default)
        => (await GetLookupAsync(ct)).Find(qan);

    /// <summary>
    /// Uploads the bundled document only if the blob is absent (If-None-Match=*), so a hand-edited
    /// or newer-than-bundled blob in a deployed environment is never clobbered. Best-effort: a
    /// storage failure is logged, not thrown, because the grade picker degrading is far better than
    /// the whole app failing to start.
    /// </summary>
    public async Task SeedIfMissingAsync(string json, CancellationToken ct = default)
    {
        try
        {
            var container = _blobServiceClient.GetBlobContainerClient(_options.RulesBlobContainer);
            await container.CreateIfNotExistsAsync(cancellationToken: ct);

            var blob = container.GetBlobClient(ResultsEnquiryBlobPaths.GradeReferenceBlobName);
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
                "Seeded grade reference '{Blob}' into container '{Container}'.",
                ResultsEnquiryBlobPaths.GradeReferenceBlobName, _options.RulesBlobContainer);
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            // Already present (or another instance won the race) — the whole point of the guard.
            _logger.LogInformation(
                "Grade reference '{Blob}' already present; skipping seed.",
                ResultsEnquiryBlobPaths.GradeReferenceBlobName);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to seed grade reference '{Blob}'; the revised-grade picker will have no options " +
                "until the blob is provisioned.", ResultsEnquiryBlobPaths.GradeReferenceBlobName);
        }
    }

    private async Task<GradeReferenceLookup> GetLookupAsync(CancellationToken ct)
    {
        if (_cache.TryGetValue(CacheKey, out GradeReferenceLookup? cached) && cached is not null)
            return cached;

        var lookup = await DownloadAsync(ct);
        _cache.Set(CacheKey, lookup, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheExpiry });
        return lookup;
    }

    private async Task<GradeReferenceLookup> DownloadAsync(CancellationToken ct)
    {
        var blob = _blobServiceClient
            .GetBlobContainerClient(_options.RulesBlobContainer)
            .GetBlobClient(ResultsEnquiryBlobPaths.GradeReferenceBlobName);

        try
        {
            var response = await blob.DownloadContentAsync(ct);
            // Malformed JSON intentionally throws so a corrupt reference file surfaces rather than
            // silently emptying every grade picker in the service.
            return GradeReferenceLookup.Parse(response.Value.Content.ToString());
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogWarning(
                "Grade reference '{Blob}' is not present in container '{Container}'; no qualification " +
                "will offer grades until it is seeded.",
                ResultsEnquiryBlobPaths.GradeReferenceBlobName, _options.RulesBlobContainer);
            return GradeReferenceLookup.Empty;
        }
    }
}
