using Azure;
using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Application.WindowManagement;
using Microsoft.Extensions.Caching.Memory;

namespace DfE.CheckPerformanceData.Infrastructure.BlobStorage;

/// <summary>
/// Reads what a checking exercise shows schools. Every tab page and every landing page asks it, so
/// it caches what can never change: a release's output (each run writes under a new release id,
/// and a release is made current only after its run completes) and an upload stored under its
/// checksum. A path that a run or an upload can overwrite in place (a legacy exercise's output, the
/// unversioned prefix the seeders write, a schema stored before checksum folders) is always read
/// from storage.
/// </summary>
/// <remarks>
/// The cache is optional because the worker host reaches this class without an IMemoryCache.
/// </remarks>
public sealed class CheckingDataReader(BlobServiceClient blobs, IMemoryCache? cache = null)
    : ICheckingDataReader
{
    private static readonly TimeSpan CacheSlidingExpiry = TimeSpan.FromMinutes(30);

    public Task<byte[]?> ReadAsync(CheckingDataExercise exercise, string laestab, CancellationToken cancellationToken)
    {
        // The exercise's current release names the output schools see.
        var name = exercise.UsesExerciseStorage
            ? CheckingExerciseBlobPaths.DataBlobName(exercise.Id,
                CheckingExerciseBlobPaths.DefaultDataType(exercise.ExerciseType), laestab, exercise.CurrentReleaseId)
            : CheckingExerciseBlobPaths.DataBlobName(exercise.ExerciseType!.Value, laestab);
        var immutable = exercise.UsesExerciseStorage && exercise.CurrentReleaseId is not null;
        return ReadCachedAsync(exercise.WindowId, name, immutable, cancellationToken);
    }

    public Task<byte[]?> ReadDatasetAsync(CheckingDataExercise exercise, Guid datasetId, string laestab,
        CancellationToken cancellationToken)
    {
        // Only a release has per-dataset files. Output from before releases is one merged file.
        if (exercise.CurrentReleaseId is not { } releaseId || !exercise.UsesExerciseStorage)
            return Task.FromResult<byte[]?>(null);
        var name = CheckingExerciseBlobPaths.DatasetBlobName(exercise.Id, releaseId, datasetId, laestab);
        return ReadCachedAsync(exercise.WindowId, name, immutable: true, cancellationToken);
    }

    public async Task<bool> HasSchoolDataAsync(Guid windowId, CheckingExerciseDto exercise, string laestab,
        CancellationToken cancellationToken)
    {
        var container = blobs.GetBlobContainerClient(windowId.ToString());

        if (!exercise.UsesExerciseStorage)
            return exercise.ExerciseType is { } kind
                && await container.GetBlobClient(CheckingExerciseBlobPaths.DataBlobName(kind, laestab))
                    .ExistsAsync(cancellationToken);

        // The merged file holds only the journey slots, so a data share has none. Its data is in
        // the per-dataset files of the current release.
        var merged = CheckingExerciseBlobPaths.DataBlobName(exercise.Id,
            CheckingExerciseBlobPaths.DefaultDataType(exercise.ExerciseType), laestab, exercise.CurrentReleaseId);
        if (exercise.CurrentReleaseId is not { } releaseId)
            return await container.GetBlobClient(merged).ExistsAsync(cancellationToken);

        // A release's files never change, so the answer for one school never does either.
        var key = $"checking-data-exists:{windowId}:{merged}";
        if (cache is not null && cache.TryGetValue(key, out bool known)) return known;

        // The merged file answers for most exercises in one call. Only a data share (which has
        // none) or a school with no journey records needs its dataset files, and those are asked
        // about together rather than one after another.
        bool exists = await container.GetBlobClient(merged).ExistsAsync(cancellationToken);
        if (!exists)
        {
            var found = await Task.WhenAll(exercise.PublishedDatasets.Select(async d =>
                (bool)await container.GetBlobClient(
                        CheckingExerciseBlobPaths.DatasetBlobName(exercise.Id, releaseId, d.Id, laestab))
                    .ExistsAsync(cancellationToken)));
            exists = found.Any(f => f);
        }

        cache?.Set(key, exists, new MemoryCacheEntryOptions { SlidingExpiration = CacheSlidingExpiry });
        return exists;
    }

    public Task<byte[]?> ReadSchemaAsync(Guid windowId, string schemaFile, CancellationToken cancellationToken)
    {
        var name = CheckingExerciseBlobPaths.SchemaBlobName(schemaFile);
        return ReadCachedAsync(windowId, name, CheckingExerciseBlobPaths.IsChecksumUpload(name), cancellationToken);
    }

    private async Task<byte[]?> ReadCachedAsync(Guid windowId, string name, bool immutable,
        CancellationToken cancellationToken)
    {
        if (!immutable || cache is null) return await DownloadAsync(windowId, name, cancellationToken);

        var key = $"checking-data:{windowId}:{name}";
        if (cache.TryGetValue(key, out byte[]? cached)) return cached;

        var bytes = await DownloadAsync(windowId, name, cancellationToken);
        cache.Set(key, bytes, new MemoryCacheEntryOptions { SlidingExpiration = CacheSlidingExpiry });
        return bytes;
    }

    // One request, not an existence check and then a download: a missing blob or container is a
    // 404 either way.
    private async Task<byte[]?> DownloadAsync(Guid windowId, string name, CancellationToken cancellationToken)
    {
        try
        {
            var blob = blobs.GetBlobContainerClient(windowId.ToString()).GetBlobClient(name);
            return (await blob.DownloadContentAsync(cancellationToken)).Value.Content.ToArray();
        }
        catch (RequestFailedException e) when (e.Status == 404)
        {
            return null;
        }
    }
}
