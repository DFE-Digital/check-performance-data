using System.Text;
using System.Text.Json;
using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Application.Dashboard;
using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using Microsoft.Extensions.Caching.Memory;

namespace DfE.CheckPerformanceData.Infrastructure.BlobStorage;

/// <summary>
/// Reads the per-school 16-19 results blob, mirroring <see cref="PupilDataBlobClient"/>. Caching
/// lives here rather than in a repository (as it does for pupils) because there is no repository
/// between this and its callers — the same 30-minute sliding window is used so a school's results
/// and pupils go stale together.
/// </summary>
public sealed class StudentResultsBlobClient(BlobServiceClient blobServiceClient, IMemoryCache cache) : IStudentResultsClient
{
    private static readonly TimeSpan CacheSlidingExpiry = TimeSpan.FromMinutes(30);

    /// <summary>Public so tests bind fixture JSON exactly as production does.</summary>
    public static JsonSerializerOptions JsonOptions => ResultsEnquiryJson.Options;

    public async Task<IReadOnlyList<StudentResultRecord>> GetResultsAsync(
        Guid windowId, string laestab, string cypmdId, CancellationToken ct = default)
    {
        var all = await GetSchoolResultsAsync(windowId, laestab, ct);
        return all.Where(r => string.Equals(r.CypmdId, cypmdId, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public async Task<bool> AnyForSourceAsync(
        Guid windowId, string laestab, string sourceTag, CancellationToken ct = default)
    {
        var all = await GetSchoolResultsAsync(windowId, laestab, ct);
        return all.Any(r => string.Equals(r.SourceFile, sourceTag, StringComparison.Ordinal));
    }

    public async Task UploadResultsAsync(
        Guid windowId, string laestab, IReadOnlyList<StudentResultRecord> results, CancellationToken ct = default)
    {
        var container = blobServiceClient.GetBlobContainerClient(windowId.ToString());
        await container.CreateIfNotExistsAsync(cancellationToken: ct);

        var blob = container.GetBlobClient(ResultsEnquiryBlobPaths.ResultsBlobName(laestab));
        var json = JsonSerializer.Serialize(results, JsonOptions);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        await blob.UploadAsync(stream, overwrite: true, cancellationToken: ct);

        // A seeding write must not leave the reader serving the pre-seed value for 30 minutes.
        cache.Remove(CacheKey(windowId, laestab));
    }

    private async Task<IReadOnlyList<StudentResultRecord>> GetSchoolResultsAsync(
        Guid windowId, string laestab, CancellationToken ct)
    {
        var key = CacheKey(windowId, laestab);
        if (cache.TryGetValue(key, out IReadOnlyList<StudentResultRecord>? cached) && cached is not null)
            return cached;

        var results = await DownloadAsync(windowId, laestab, ct);
        cache.Set(key, results, new MemoryCacheEntryOptions { SlidingExpiration = CacheSlidingExpiry });
        return results;
    }

    private async Task<IReadOnlyList<StudentResultRecord>> DownloadAsync(
        Guid windowId, string laestab, CancellationToken ct)
    {
        var container = blobServiceClient.GetBlobContainerClient(windowId.ToString());
        if (!await container.ExistsAsync(ct))
            return [];

        var blob = container.GetBlobClient(ResultsEnquiryBlobPaths.ResultsBlobName(laestab));
        if (!await blob.ExistsAsync(ct))
            return [];

        var response = await blob.DownloadContentAsync(ct);
        // Malformed JSON intentionally throws so corrupt files surface rather than read as empty.
        return JsonSerializer.Deserialize<List<StudentResultRecord>>(
            response.Value.Content.ToMemory().Span, JsonOptions) ?? [];
    }

    // The laestab is normalised so a claim value of "933/4070" and a blob name of "9334070" agree.
    private static string CacheKey(Guid windowId, string laestab)
        => $"results:{windowId}:{LaestabNormaliser.Normalise(laestab)}";
}
