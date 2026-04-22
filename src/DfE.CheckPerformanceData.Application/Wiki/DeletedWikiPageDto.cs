namespace DfE.CheckPerformanceData.Application.Wiki;

public sealed class DeletedWikiPageDto
{
    public int Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string OriginalSlugPath { get; init; } = string.Empty;
    public DateTime? DeletedAt { get; init; }
    public string? DeletedBy { get; init; }
    public int DescendantCount { get; init; }
}
