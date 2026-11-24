using DfE.CheckPerformanceData.Application.Media.Embedded;

namespace DfE.CheckPerformanceData.Application.Media.Reports;

/// <summary>A current page version (live, scheduled or the working draft) that uses library images, with its content.</summary>
public sealed record MediaReportPageVersion(
    Guid PageNodeId,
    string Title,
    EmbeddedVersionState State,
    DateTime UpdatedAtUtc,
    string Content);
