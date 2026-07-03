namespace DfE.CheckPerformanceData.Application.Search;

// Cross-content-type search that the /search endpoint (and the CMS search + PageNav search widgets)
// call into. Scope limits results to a path prefix — "guidance" restricts to pages under /guidance
// and to blocks last seen under /guidance/*. Null/empty scope searches everything.
public interface ISiteSearchService
{
    Task<SiteSearchResult> SearchAsync(SiteSearchQuery query);
}

public sealed record SiteSearchQuery(
    string? Query,
    string? ScopePath = null,
    bool IncludePages = true,
    bool IncludeContentBlocks = true,
    int MaxPerType = 20);

public sealed class SiteSearchResult
{
    public required string CurrentQuery { get; init; }
    public required string? ScopePath { get; init; }
    public required Wiki.SearchInvalidReason? InvalidReason { get; init; }
    public required IReadOnlyList<PageSearchHitDto> PageHits { get; init; }
    public required IReadOnlyList<ContentBlocks.ContentBlockSearchResultDto> ContentBlockHits { get; init; }
    public int TotalHits => PageHits.Count + ContentBlockHits.Count;
}
