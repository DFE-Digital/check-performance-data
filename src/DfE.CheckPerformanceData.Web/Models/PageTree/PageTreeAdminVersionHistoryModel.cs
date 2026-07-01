using DfE.CheckPerformanceData.Application.PageTree;

namespace DfE.CheckPerformanceData.Web.Models.PageTree;

/// <summary>Model for the inline version-history list partial rendered on both edit pages.</summary>
public sealed class PageTreeAdminVersionHistoryModel
{
    public required Guid NodeId { get; init; }
    public required IReadOnlyList<PageNodeVersionDto> Versions { get; init; }
}
