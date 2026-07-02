using DfE.CheckPerformanceData.Application.ContentPages;

namespace DfE.CheckPerformanceData.Web.Models.PageTree;

// View model passed to Views/Page/{Content,Wiki,Folder}.cshtml by the catch-all PageController.
// Content and Nav are populated only for page type "content"; WikiHtml only for "wiki".
// IsPreview=true when the viewer is an editor previewing an unpublished page.
public sealed class RenderedPageViewModel
{
    public required string Title { get; init; }
    public string? Subtitle { get; init; }
    public required string PageType { get; init; }
    public IReadOnlyList<ContentNode>? Content { get; init; }
    public IReadOnlyList<ContentNavItem>? Nav { get; init; }
    public string? WikiHtml { get; init; }
    public bool IsPreview { get; init; }

    /// <summary>
    /// GDS breadcrumb items from Home down to the current page's immediate parent, in root-first
    /// order. The current page's title is rendered as the trailing plain-text crumb by the view.
    /// Empty for root nodes; a "Home" crumb pointing at "/" is prepended by the view.
    /// </summary>
    public IReadOnlyList<BreadcrumbItem> Breadcrumb { get; init; } = [];
}

public sealed record BreadcrumbItem(string Title, string Href);
