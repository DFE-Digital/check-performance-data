namespace DfE.CheckPerformanceData.Application.Media.Reports;

/// <summary>
/// The accessibility report: library images with missing or weak default alt text, and the uses on
/// current page versions and content blocks where readers get missing or weak alt text. Both lists
/// put the most recently changed first.
/// </summary>
public sealed record MediaAltTextReport(
    IReadOnlyList<MediaAltTextAssetRow> Assets,
    IReadOnlyList<MediaAltTextUseRow> Uses);
