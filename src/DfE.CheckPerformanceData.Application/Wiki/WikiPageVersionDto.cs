namespace DfE.CheckPerformanceData.Application.Wiki;

public sealed class WikiPageVersionDto
{
    public int Id { get; init; }
    public int VersionNumber { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Content { get; init; }
    public string? ContentHtml { get; init; }
    public DateTime CreatedAt { get; init; }
    public string? CreatedBy { get; init; }
}
