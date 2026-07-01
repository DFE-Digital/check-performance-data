using DfE.CheckPerformanceData.Application.PageTree;

namespace DfE.CheckPerformanceData.Web.Models.PageTree;

public sealed class PageTreeAdminVersionsViewModel
{
    public Guid NodeId { get; init; }
    public required string NodeTitle { get; init; }
    public required List<PageNodeVersionDto> Versions { get; init; }
}
