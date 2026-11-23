using DfE.CheckPerformanceData.Application.Media.Reports;

namespace DfE.CheckPerformanceData.Application.Media.Variants;

/// <summary>
/// Reads the current content that records a use of the images, and groups each image's uses with
/// <see cref="MediaVariantGrouper"/>. The settings of each use are in the content, not in
/// <c>media_usages</c>, so they are read from the content each time. It takes two queries for any
/// number of images: one for page versions and one for content blocks.
/// </summary>
public sealed class MediaVariantService(IMediaReportStore store, TimeProvider time) : IMediaVariantService
{
    public async Task<IReadOnlyList<MediaVariant>> GetVariantsAsync(MediaAssetDto asset, CancellationToken ct = default)
    {
        var (pages, blocks) = await ReadAsync([asset.Id], ct);
        return MediaVariantGrouper.Group(asset.Latest, pages, blocks);
    }

    public async Task<IReadOnlyDictionary<Guid, int>> CountVariantsAsync(IReadOnlyCollection<MediaAssetDto> assets, CancellationToken ct = default)
    {
        if (assets.Count == 0) return new Dictionary<Guid, int>();

        var (pages, blocks) = await ReadAsync(assets.Select(a => a.Id).Distinct().ToList(), ct);
        var counts = new Dictionary<Guid, int>();
        foreach (var asset in assets)
        {
            counts[asset.Id] = MediaVariantGrouper.Group(
                asset.Latest,
                pages.Where(p => p.RecordedAssetIds.Contains(asset.Id)),
                blocks.Where(b => b.RecordedAssetIds.Contains(asset.Id))).Count;
        }
        return counts;
    }

    private async Task<(IReadOnlyList<MediaVariantPageVersion> Pages, IReadOnlyList<MediaVariantBlock> Blocks)> ReadAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        var pages = await store.ListCurrentPageVersionsUsingAsync(ids, time.GetUtcNow().UtcDateTime, ct);
        var blocks = await store.ListContentBlocksUsingAsync(ids, ct);
        return (pages, blocks);
    }
}
