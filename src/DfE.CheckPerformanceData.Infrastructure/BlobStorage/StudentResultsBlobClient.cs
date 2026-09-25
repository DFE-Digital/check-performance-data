using System.Text;
using System.Text.Json;
using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Application.Dashboard;
using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using Microsoft.Extensions.Caching.Memory;

namespace DfE.CheckPerformanceData.Infrastructure.BlobStorage;

/// <summary>
/// Reads the per-school 16-19 results blob, mirroring <see cref="PupilDataBlobClient"/>. Caching
/// lives here rather than in a repository (as it does for pupils) because there is no repository
/// between this and its callers — the same 30-minute sliding window is used so a school's results
/// and pupils go stale together.
/// </summary>
public sealed class StudentResultsBlobClient(
    BlobServiceClient blobServiceClient, IMemoryCache cache, ICheckingExerciseStorageResolver? resolver = null)
    : IStudentResultsClient
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

    public async Task<IReadOnlySet<string>> GetStudentIdsWithResultsAsync(
        Guid windowId, string laestab, CancellationToken ct = default)
    {
        var all = await GetSchoolResultsAsync(windowId, laestab, ct);
        return all.Select(r => r.CypmdId).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public Task<IReadOnlyList<StudentResultRecord>> GetAllResultsAsync(
        Guid windowId, string laestab, CancellationToken ct = default)
        => GetSchoolResultsAsync(windowId, laestab, ct);

    public async Task UploadResultsAsync(
        Guid windowId, string laestab, IReadOnlyList<StudentResultRecord> results, CancellationToken ct = default)
    {
        var container = blobServiceClient.GetBlobContainerClient(windowId.ToString());
        await container.CreateIfNotExistsAsync(cancellationToken: ct);

        var path = await OutputPathAsync(windowId, laestab, ct)
            ?? throw new InvalidOperationException("No checking exercise owns this upload.");
        var blob = container.GetBlobClient(path);
        var json = JsonSerializer.Serialize(results, JsonOptions);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        await blob.UploadAsync(stream, overwrite: true, cancellationToken: ct);

        // A seeding write must not leave the reader serving the pre-seed value for 30 minutes.
        cache.Remove(CacheKey(windowId, laestab, path));
    }

    private async Task<IReadOnlyList<StudentResultRecord>> GetSchoolResultsAsync(
        Guid windowId, string laestab, CancellationToken ct)
    {
        var path = await OutputPathAsync(windowId, laestab, ct);
        if (path is null) return [];

        var key = CacheKey(windowId, laestab, path);
        if (cache.TryGetValue(key, out IReadOnlyList<StudentResultRecord>? cached) && cached is not null)
            return cached;

        var results = await DownloadAsync(windowId, path, ct);
        cache.Set(key, results, new MemoryCacheEntryOptions { SlidingExpiration = CacheSlidingExpiry });
        return results;
    }

    private async Task<IReadOnlyList<StudentResultRecord>> DownloadAsync(
        Guid windowId, string path, CancellationToken ct)
    {
        var container = blobServiceClient.GetBlobContainerClient(windowId.ToString());
        if (!await container.ExistsAsync(ct))
            return [];

        var blob = container.GetBlobClient(path);
        if (!await blob.ExistsAsync(ct))
            return [];

        var response = await blob.DownloadContentAsync(ct);
        // Malformed JSON intentionally throws so corrupt files surface rather than read as empty.
        return JsonSerializer.Deserialize<List<StudentResultRecord>>(
            response.Value.Content.ToMemory().Span, JsonOptions) ?? [];
    }

    // Resolves first, exactly like PupilDataBlobClient: a null resolver reproduces the legacy path
    // exactly (every window configured before #466 depends on it), and a resolver that comes back
    // ambiguous or unconfigured yields null rather than a guessed path.
    private async Task<string?> OutputPathAsync(Guid windowId, string laestab, CancellationToken ct)
    {
        var target = resolver is null ? null : await resolver.ResolveAsync(windowId, CheckingExerciseType.ResultsEnquiry, ct);
        if (resolver is not null && target is null) return null;
        return target is { UsesExerciseStorage: true }
            ? CheckingExerciseBlobPaths.DataBlobName(target.Id, CheckingDataType.Results,
                LaestabNormaliser.Normalise(laestab), target.CurrentReleaseId)
            : ResultsEnquiryBlobPaths.ResultsBlobName(laestab);
    }

    // The laestab is normalised so a claim value of "933/4070" and a blob name of "9334070" agree.
    // The path is folded in so a resolved exercise's cache entry never collides with another
    // exercise's — or the legacy path's — entry for the same window and school. The path also
    // names the current release, so a new release is a new key and is read at once.
    private static string CacheKey(Guid windowId, string laestab, string path)
        => $"results:{windowId}:{LaestabNormaliser.Normalise(laestab)}:{path}";
}
