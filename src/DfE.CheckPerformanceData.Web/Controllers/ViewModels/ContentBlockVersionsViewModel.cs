using DfE.CheckPerformanceData.Application.ContentBlocks;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels;

public sealed class ContentBlockVersionsViewModel
{
    public ContentBlockDto Block { get; init; } = null!;
    public List<ContentBlockVersionDto> Versions { get; init; } = [];
}
