using DfE.CheckPerformanceData.Application.Media.Embedded;
using DfE.CheckPerformanceData.Application.Media.Reports;
using DfE.CheckPerformanceData.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Persistence.Repositories;

// The reads behind the media library reports. Nothing here writes. The content to check is picked
// from media_usages, the same rows the image page uses for "where used", so the report and that
// list agree. "Current" is the rule in PageVersionStates. The page query filter leaves out deleted
// pages. Sizes are the stored files recorded in media_asset_versions.
public sealed class MediaReportStore(IPortalDbContext context) : IMediaReportStore
{
    public async Task<IReadOnlyList<MediaReportAsset>> ListAssetsAsync(CancellationToken ct = default) =>
        await context.MediaAssets
            .AsNoTracking()
            .Select(a => new MediaReportAsset(a.Id, a.FileName, a.DefaultAltText, a.Caption, a.UpdatedAtUtc, a.FolderId))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<MediaReportPageVersion>> ListCurrentPageVersionsAsync(DateTime nowUtc, CancellationToken ct = default)
    {
        var current = PageVersionStates.CurrentVersionIds(context, nowUtc);
        var used = context.MediaUsages
            .Where(u => u.PageNodeVersionId != null)
            .Select(u => u.PageNodeVersionId!.Value);

        var versions = await context.PageNodeVersions
            .AsNoTracking()
            .Where(v => current.Contains(v.Id) && used.Contains(v.Id))
            .Select(v => new { v.PageNodeId, v.PageNode.Title, v.PublishFrom, v.UpdatedDate, v.Content })
            .ToListAsync(ct);

        return versions
            .Select(v => new MediaReportPageVersion(v.PageNodeId, v.Title, State(v.PublishFrom, nowUtc), v.UpdatedDate, v.Content))
            .ToList();
    }

    public async Task<IReadOnlyList<MediaReportBlock>> ListContentBlocksAsync(CancellationToken ct = default)
    {
        var used = context.MediaUsages
            .Where(u => u.ContentBlockId != null)
            .Select(u => u.ContentBlockId!.Value);

        return await context.ContentBlocks
            .AsNoTracking()
            .Where(b => used.Contains(b.Id))
            .Select(b => new MediaReportBlock(b.Id, b.Key, b.UpdatedAt, b.Value))
            .ToListAsync(ct);
    }

    public async Task<MediaSizeTotals> GetSizeTotalsAsync(CancellationToken ct = default)
    {
        var images = await context.MediaAssets.CountAsync(ct);
        var versions = await context.MediaAssetVersions.CountAsync(ct);
        var stored = await context.MediaAssetVersions.SumAsync(v => (long?)v.SizeBytes, ct) ?? 0;
        var current = await context.MediaAssetVersions
            .Where(v => v.Version == v.Asset.CurrentVersion)
            .SumAsync(v => (long?)v.SizeBytes, ct) ?? 0;
        return new MediaSizeTotals(images, versions, current, stored);
    }

    public async Task<IReadOnlyList<MediaSizeAsset>> ListLargestAsync(int take, CancellationToken ct = default) =>
        await context.MediaAssetVersions
            .AsNoTracking()
            .Where(v => v.Version == v.Asset.CurrentVersion)
            .OrderByDescending(v => v.SizeBytes)
            .ThenBy(v => v.Asset.FileName)
            .Take(take)
            .Select(v => new MediaSizeAsset(
                v.AssetId,
                v.Asset.FileName,
                v.ContentType,
                v.Version,
                v.SizeBytes,
                v.Asset.Versions.Count,
                v.Asset.Versions.Sum(o => o.SizeBytes),
                v.Asset.FolderId))
            .ToListAsync(ct);

    private static EmbeddedVersionState State(DateTime? publishFrom, DateTime nowUtc) =>
        publishFrom is null ? EmbeddedVersionState.Draft
        : publishFrom > nowUtc ? EmbeddedVersionState.Scheduled
        : EmbeddedVersionState.Live;
}
