namespace DfE.CheckPerformanceData.Application.Search;

// Cross-content-type search that the /search endpoint (and the CMS search + PageNav search widgets)
// call into. Scope limits results to a path prefix — "guidance" restricts to pages under /guidance
// and to blocks last seen under /guidance/*. Null/empty scope searches everything.
public interface ISiteSearchService
{
    Task<SiteSearchResult> SearchAsync(SiteSearchQuery query);

    // Merged view of the same search: pages + content blocks interleaved by rank into a single
    // sequence, then paged. Powers the content-page results widget where the user shouldn't need
    // to know whether a hit came from a page body or a block on a page.
    Task<SiteSearchPagedResult> SearchMergedPagedAsync(SiteSearchQuery query, int page, int pageSize);
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
    public required SearchInvalidReason? InvalidReason { get; init; }
    // Single canonical hit list, one row per URL. Any block that resolves to a URL
    // shared with a page (or another block) folds into that URL's row via the
    // canonicaliser; the rendered result is always a list of URLs, never a mix of
    // page hits and standalone block hits.
    public required IReadOnlyList<CanonicalSearchHit> Hits { get; init; }
    public int TotalHits => Hits.Count;
}

// One hit in the merged, container-agnostic view. Title/Url/Snippet mirror what the split
// DTOs surface but drop the "which corpus did this come from" distinction.
public sealed record SiteSearchHit(
    string Title,
    string Url,
    string? Subtitle,
    string SnippetHtml,
    float Rank);

public sealed class SiteSearchPagedResult
{
    public required string CurrentQuery { get; init; }
    public required string? ScopePath { get; init; }
    public required SearchInvalidReason? InvalidReason { get; init; }
    public required IReadOnlyList<SiteSearchHit> Items { get; init; }
    public required int TotalCount { get; init; }
    // Zero-indexed for internal math (matches the shared _Pagination view model). URLs
    // are one-indexed at the widget boundary.
    public required int Page { get; init; }
    public required int PageSize { get; init; }
    public int TotalPages => PageSize > 0 && TotalCount > 0
        ? (TotalCount + PageSize - 1) / PageSize
        : 0;
}
