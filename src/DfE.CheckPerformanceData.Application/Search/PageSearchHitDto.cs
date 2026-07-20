namespace DfE.CheckPerformanceData.Application.Search;

// A single page hit for the unified search. Path is stored without a leading slash
// (matching PageNode.Path); the search view is responsible for prefixing "/".
public sealed class PageSearchHitDto
{
    public required Guid PageId { get; init; }
    public required string Path { get; init; }
    public required string Title { get; init; }
    public string? Subtitle { get; init; }
    public required string SnippetHtml { get; init; }

    /// <summary>Combined ts_rank score. Surfaced in an HTML comment on the search view for debugging.</summary>
    public float Rank { get; init; }
}
