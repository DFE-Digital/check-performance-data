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
    public string? PageName { get; init; }
    public required string PageType { get; init; }

    /// <summary>The label to show in tree / nav / list surfaces. PageName if set, else Title.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(PageName) ? Title : PageName;
}
