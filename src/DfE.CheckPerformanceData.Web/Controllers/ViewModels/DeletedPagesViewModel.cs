using DfE.CheckPerformanceData.Application.PageTree;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels;

public sealed class DeletedPagesViewModel
{
    public required IReadOnlyList<PageNodeDto> Deleted { get; init; }
    public string? SuccessMessage { get; init; }
}
