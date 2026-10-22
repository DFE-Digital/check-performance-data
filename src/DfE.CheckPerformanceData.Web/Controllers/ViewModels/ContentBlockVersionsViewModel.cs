using DfE.CheckPerformanceData.Application.ContentBlocks;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels;

public sealed class ContentBlockVersionsViewModel
{
    public ContentBlockDto Block { get; init; } = null!;
    public List<ContentBlockVersionDto> Versions { get; init; } = [];

    /// <summary>
    /// Where the "Back" link goes: the page the editor was working on, or the content-blocks
    /// index. Always a validated local path.
    /// </summary>
    public string ReturnUrl { get; init; } = "/admin/content-blocks";
}
