namespace DfE.CheckPerformanceData.Application.Media.Reports;

/// <summary>
/// The size report: totals for the library and its resized copies, and the largest images by the
/// size of the file they show now.
/// </summary>
public sealed record MediaSizeReport(
    MediaSizeTotals Totals,
    MediaRenditionTally Renditions,
    IReadOnlyList<MediaSizeRow> Largest);
