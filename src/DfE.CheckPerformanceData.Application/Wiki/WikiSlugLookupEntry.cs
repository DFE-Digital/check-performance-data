namespace DfE.CheckPerformanceData.Application.Wiki;

public sealed class WikiSlugLookupEntry
{
    public int Id { get; init; }
    public string Slug { get; init; } = string.Empty;
    public int? ParentId { get; init; }
}
