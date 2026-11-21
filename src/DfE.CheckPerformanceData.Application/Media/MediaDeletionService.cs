using Microsoft.Extensions.Logging;

namespace DfE.CheckPerformanceData.Application.Media;

/// <summary>
/// Deletes a library image. The rows go first, in one save that the generic audit capture
/// records, and only when no page version uses the image. Then every original and rendition under
/// the image's blob prefixes is removed. A row must never point at a missing file, but a file with
/// no row is harmless: nothing reads it. So a storage failure after the rows are gone is logged
/// with the prefix, for removal by hand, and the delete still succeeds.
/// </summary>
public sealed class MediaDeletionService(
    IMediaAssetRepository assets,
    IMediaBlobStore blobs,
    ILogger<MediaDeletionService> logger) : IMediaDeletionService
{
    /// <summary>The most images one "Delete unused images" run takes. It keeps the form under the post limits.</summary>
    public const int MaxUnusedPerRun = 500;

    // Each image goes through DeleteAsync, which checks its uses again inside the delete. So an
    // image that content started to use after the confirmation page stays. The audit row is not
    // cancelled: the images it lists are already gone.
    public async Task<MediaUnusedDeleteResult> DeleteUnusedAsync(IReadOnlyCollection<Guid> assetIds, CancellationToken ct = default)
    {
        var runId = Guid.NewGuid();
        var ids = assetIds.Where(id => id != Guid.Empty).Distinct().Take(MaxUnusedPerRun).ToList();
        if (ids.Count == 0) return new MediaUnusedDeleteResult(runId, [], [], []);

        List<Guid> deleted = [], inUse = [], notFound = [];
        foreach (var id in ids)
        {
            if (ct.IsCancellationRequested) break;
            var outcome = await DeleteAsync(id, CancellationToken.None);
            (outcome switch
            {
                MediaDeleteOutcome.Deleted => deleted,
                MediaDeleteOutcome.InUse => inUse,
                _ => notFound,
            }).Add(id);
        }

        var result = new MediaUnusedDeleteResult(runId, deleted, inUse, notFound);
        await assets.RecordUnusedDeleteAsync(result, CancellationToken.None);
        logger.LogInformation("Unused media run {RunId}: {Deleted} deleted, {InUse} in use, {NotFound} not found",
            runId, deleted.Count, inUse.Count, notFound.Count);
        return result;
    }

    public async Task<MediaDeleteOutcome> DeleteAsync(Guid assetId, CancellationToken ct = default)
    {
        var outcome = await assets.DeleteAsync(assetId, ct);
        if (outcome != MediaDeleteOutcome.Deleted)
        {
            logger.LogInformation("Media item {AssetId} not deleted: {Outcome}", assetId, outcome);
            return outcome;
        }

        foreach (var prefix in MediaBlobKeys.AssetPrefixes(assetId))
        {
            try
            {
                // Not cancelled: the rows are gone, so the files should go too.
                var removed = await blobs.DeleteByPrefixAsync(prefix, CancellationToken.None);
                logger.LogInformation("Media item {AssetId}: removed {Count} files under {Prefix}", assetId, removed, prefix);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Media item {AssetId} deleted, but the files under {Prefix} could not be removed", assetId, prefix);
            }
        }

        logger.LogInformation("Media item {AssetId} deleted", assetId);
        return outcome;
    }
}
