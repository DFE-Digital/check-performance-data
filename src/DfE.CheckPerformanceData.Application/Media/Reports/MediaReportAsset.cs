namespace DfE.CheckPerformanceData.Application.Media.Reports;

/// <summary>A library image as the accessibility report checks it: its name and its default text.</summary>
public sealed record MediaReportAsset(
    Guid Id,
    string FileName,
    string? DefaultAltText,
    string? Caption,
    DateTime UpdatedAtUtc);
