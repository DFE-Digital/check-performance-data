namespace DfE.CheckPerformanceData.Application.Wiki;

public sealed class WikiPageTreeNodeDto
{
    public int Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Slug { get; init; } = string.Empty;
    public string SlugPath { get; init; } = string.Empty;
    public int? ParentId { get; init; }
    public int SortOrder { get; init; }
    public List<WikiPageTreeNodeDto> Children { get; init; } = [];
}
