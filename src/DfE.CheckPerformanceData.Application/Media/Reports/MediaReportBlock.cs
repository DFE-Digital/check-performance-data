namespace DfE.CheckPerformanceData.Application.Media.Reports;

/// <summary>A content block whose current value uses library images.</summary>
public sealed record MediaReportBlock(int Id, string Key, DateTime UpdatedAtUtc, string Value);
