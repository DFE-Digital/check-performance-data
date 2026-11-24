namespace DfE.CheckPerformanceData.Application.Media.Reports;

/// <summary>
/// Builds the media library reports. It reads and never writes. The accessibility report checks
/// the library and the current content at this moment. The size report reads sizes from the
/// database, and adds up the resized copies from one listing of the blob container, because the
/// database does not record them.
/// </summary>
public sealed class MediaReportService(IMediaReportStore store, IMediaBlobStore blobs, TimeProvider time) : IMediaReportService
{
    public async Task<MediaAltTextReport> GetAltTextReportAsync(CancellationToken ct = default)
    {
        var assets = await store.ListAssetsAsync(ct);
        var pages = await store.ListCurrentPageVersionsAsync(time.GetUtcNow().UtcDateTime, ct);
        var blocks = await store.ListContentBlocksAsync(ct);
        return MediaAltTextReportBuilder.Build(assets, pages, blocks);
    }

    public async Task<MediaSizeReport> GetSizeReportAsync(int take, CancellationToken ct = default)
    {
        var totals = await store.GetSizeTotalsAsync(ct);
        var largest = await store.ListLargestAsync(take, ct);
        var renditions = await blobs.ListAsync(MediaBlobKeys.AllRenditionsPrefix, ct);

        var byAsset = renditions
            .Select(r => (Ok: MediaBlobKeys.TryGetRenditionAsset(r.Key, out var id), Id: id, r.SizeBytes))
            .Where(r => r.Ok)
            .GroupBy(r => r.Id)
            .ToDictionary(g => g.Key, g => new MediaRenditionTally(g.Count(), g.Sum(r => r.SizeBytes)));

        return new MediaSizeReport(
            totals,
            new MediaRenditionTally(renditions.Count, renditions.Sum(r => r.SizeBytes)),
            largest.Select(a => new MediaSizeRow(a, byAsset.GetValueOrDefault(a.Id, MediaRenditionTally.None))).ToList());
    }
}
