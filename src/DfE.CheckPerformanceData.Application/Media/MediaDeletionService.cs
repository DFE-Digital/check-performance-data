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
