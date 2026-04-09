namespace DfE.CheckPerformanceData.Application.Wiki;

public sealed class CreateWikiPageDto
{
    public string Title { get; init; } = string.Empty;
    public string? Content { get; init; }
    public int? ParentId { get; init; }
}
