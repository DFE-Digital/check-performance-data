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
    public required string PageType { get; init; }
    public bool HasLiveVersion { get; init; }
}
