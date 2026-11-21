namespace DfE.CheckPerformanceData.Application.Media.Reports;

/// <summary>The library records and the content that the media reports read. It changes nothing.</summary>
public interface IMediaReportStore
{
    /// <summary>Every library image, with its default alt text and caption.</summary>
    Task<IReadOnlyList<MediaReportAsset>> ListAssetsAsync(CancellationToken ct = default);

    /// <summary>
    /// Current page versions (live, scheduled and the working draft) of pages that are not deleted,
    /// that have a recorded use of a library image, with their content.
    /// </summary>
    Task<IReadOnlyList<MediaReportPageVersion>> ListCurrentPageVersionsAsync(DateTime nowUtc, CancellationToken ct = default);

    /// <summary>Content blocks with a recorded use of a library image, with their value.</summary>
    Task<IReadOnlyList<MediaReportBlock>> ListContentBlocksAsync(CancellationToken ct = default);

    Task<MediaSizeTotals> GetSizeTotalsAsync(CancellationToken ct = default);

    /// <summary>The largest images by the size of their current file, largest first.</summary>
    Task<IReadOnlyList<MediaSizeAsset>> ListLargestAsync(int take, CancellationToken ct = default);
}
