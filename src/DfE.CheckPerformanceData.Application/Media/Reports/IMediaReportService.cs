namespace DfE.CheckPerformanceData.Application.Media.Reports;

/// <summary>The media library reports: alt text that does not describe its image, and storage size.</summary>
public interface IMediaReportService
{
    Task<MediaAltTextReport> GetAltTextReportAsync(CancellationToken ct = default);

    /// <summary>The totals, and the <paramref name="take"/> largest images.</summary>
    Task<MediaSizeReport> GetSizeReportAsync(int take, CancellationToken ct = default);
}
