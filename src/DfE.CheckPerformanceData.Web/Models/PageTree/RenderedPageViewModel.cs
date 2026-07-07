using DfE.CheckPerformanceData.Application.ContentPages;

namespace DfE.CheckPerformanceData.Web.Models.PageTree;

// View model passed to Views/Page/{Content,Wiki,Folder}.cshtml by the catch-all PageController.
// Content and Nav are populated only for page type "content"; WikiHtml only for "wiki".
// IsPreview=true when the viewer is an editor previewing an unpublished page.
public sealed class RenderedPageViewModel
{
    /// <summary>PageNode id — used to build the /admin/pages/{id}/edit deep-link on the
    /// in-page edit shortcut shown to CMS editors.</summary>
    public Guid NodeId { get; init; }
    public required string Title { get; init; }
    public string? Subtitle { get; init; }
    public required string PageType { get; init; }
    public IReadOnlyList<ContentNode>? Content { get; init; }
    /// <summary>
    /// Auto-generated heading nav (H2 top-level, H3 nested) for content pages, the sibling
    /// nav for wiki pages, or the child list for folder pages. Consumed by the pagenav widget
    /// via ViewData on content pages; used directly by the Wiki and Folder templates.
    /// </summary>
    public IReadOnlyList<ContentNavItem>? Nav { get; init; }

    /// <summary>
    /// Direct children of the current node — used by the pagenav widget when its <c>mode</c>
    /// prop is set to <c>"children"</c>. Content pages only.
    /// </summary>
    public IReadOnlyList<ContentNavItem>? ChildrenNav { get; init; }
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
