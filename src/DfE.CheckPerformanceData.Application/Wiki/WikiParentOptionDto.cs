namespace DfE.CheckPerformanceData.Application.Wiki;

public sealed class WikiParentOptionDto
{
    public int Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string SlugPath { get; init; } = string.Empty;
    public int Depth { get; init; }
}
