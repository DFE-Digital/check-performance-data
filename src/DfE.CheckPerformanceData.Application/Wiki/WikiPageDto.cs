namespace DfE.CheckPerformanceData.Application.Wiki;

public sealed class WikiPageDto
{
    public int Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Slug { get; init; } = string.Empty;
    public string SlugPath { get; init; } = string.Empty;
    public string? Content { get; init; }
    public string? ContentHtml { get; init; }
    public int? ParentId { get; init; }
    public int SortOrder { get; init; }
}
