namespace DfE.CheckPerformanceData.Web.Models.PageTree;

/// <summary>
/// View model for the page-tree admin grid (GET /admin/pages and GET /admin/pages/{id}).
/// Shows the DIRECT CHILDREN of the selected node in a paged, searchable table.
/// When <see cref="SelectedId"/> is null the view is at the root level (top-level pages).
/// </summary>
public sealed class PageTreeGridViewModel
{
    /// <summary>Id of the currently-selected node; null = root (top-level pages).</summary>
    public Guid? SelectedId { get; init; }

    /// <summary>"All pages" at root, or the node's own title when drilling into a node.</summary>
    public required string SelectedTitle { get; init; }

    /// <summary>Path of the selected node; null at root.</summary>
    public string? SelectedPath { get; init; }

    /// <summary>PageType of the selected node; null at root.</summary>
    public string? SelectedPageType { get; init; }

    /// <summary>
    /// ParentId of the selected node, used to render the "Back to parent" link.
    /// Null when the selected node is itself a root node (no parent).
    /// </summary>
    public Guid? SelectedParentId { get; init; }

    /// <summary>Whether the selected node has a live version (used for the node's own View link).</summary>
    public bool SelectedHasLiveVersion { get; init; }

    /// <summary>The page of direct children (already filtered by search and paged).</summary>
    public required IReadOnlyList<PageTreeGridRowViewModel> Children { get; init; }

    /// <summary>The active search term; null or empty when no search is applied.</summary>
    public string? SearchQuery { get; init; }

    public int CurrentPage { get; init; }
    public int TotalPages { get; init; }

    /// <summary>Count of children AFTER the search filter; zero if no children match.</summary>
    public int TotalCount { get; init; }

    public int PageSize { get; init; }
}

/// <summary>One row in the page-tree admin grid.</summary>
public sealed class PageTreeGridRowViewModel
{
    public Guid Id { get; init; }
    public required string Title { get; init; }
    public required string Segment { get; init; }
    public required string Path { get; init; }
    public required string PageType { get; init; }
    public bool HasLiveVersion { get; init; }
}
