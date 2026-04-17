namespace DfE.CheckPerformanceData.Application.Wiki;

public sealed class UpdateWikiPageDto
{
    public string Title { get; init; } = string.Empty;
    public string? Content { get; init; }
}
