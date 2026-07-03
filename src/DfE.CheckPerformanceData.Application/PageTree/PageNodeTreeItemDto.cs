namespace DfE.CheckPerformanceData.Application.PageTree;

// Flat row for tree builder — caller assembles into hierarchy via ParentId links.
public sealed class PageNodeTreeItemDto
{
    public Guid Id { get; init; }
    public Guid? ParentId { get; init; }
    public required string Segment { get; init; }
    public required string Path { get; init; }
    public int SortOrder { get; init; }
    public required string Title { get; init; }
    public string? Subtitle { get; init; }
    public string? PageName { get; init; }
    public required string PageType { get; init; }

    /// <summary>Label for tree/nav renderers: PageName if set, else Title.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(PageName) ? Title : PageName;
    public bool HasLiveVersion { get; init; }
    /// <summary>Used as a secondary sort key when SortOrder values are equal (e.g. legacy data all-zero).</summary>
    public DateTime CreatedDate { get; init; }
}
