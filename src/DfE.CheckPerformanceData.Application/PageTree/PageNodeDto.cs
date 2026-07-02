namespace DfE.CheckPerformanceData.Application.PageTree;

public sealed class PageNodeDto
{
    public Guid Id { get; init; }
    public Guid? ParentId { get; init; }
    public required string Segment { get; init; }
    public required string Path { get; init; }
    public int SortOrder { get; init; }
    public required string Title { get; init; }
    public string? Subtitle { get; init; }
    public required string PageType { get; init; }
}
