using DfE.CheckPerformanceData.Application.ContentPages;

namespace DfE.CheckPerformanceData.Web.Models.PageTree;

// View model passed to Views/Page/{Content,Wiki,Folder}.cshtml by the catch-all PageController.
// Content and Nav are populated only for page type "content"; WikiHtml only for "wiki".
public sealed class RenderedPageViewModel
{
    public required string Title { get; init; }
    public required string PageType { get; init; }
    public IReadOnlyList<ContentNode>? Content { get; init; }
    public IReadOnlyList<ContentNavItem>? Nav { get; init; }
    public string? WikiHtml { get; init; }
}
