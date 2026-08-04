namespace DfE.CheckPerformanceData.Application.Search;

// Cross-content-type search that the /search endpoint (and the CMS search + PageNav search widgets)
// call into. Scope limits results to a path prefix — "guidance" restricts to pages under /guidance
// and to blocks last seen under /guidance/*. Null/empty scope searches everything.
//
// Single paged primitive: both /search and the content-page results widget call SearchAsync so
// there is one place to guard the DB call, one place to sanitise input, one place to canonicalise
// and page.
public interface ISiteSearchService
{
    Task<SiteSearchPagedResult> SearchAsync(SiteSearchQuery query);
}

// Positional record: the trailing three ints carry the paging window.
//   MaxPerType — per-corpus fetch window feeding the canonicaliser. Default 500 is the retired
//                MergedFetchCap — represents the ceiling from which the in-memory pager slices.
//   Page       — one-indexed at the API boundary (matches the widget's ?page=N URL convention);
//                1 = first page.
//   PageSize   — default 20; controllers clamp [10, 50] before constructing this record.
public sealed record SiteSearchQuery(
    string? Query,
    string? ScopePath = null,
    bool IncludePages = true,
    bool IncludeContentBlocks = true,
    int MaxPerType = 500, int Page = 1, int PageSize = 20);

public sealed class SiteSearchPagedResult
{
    public required string CurrentQuery { get; init; }
    public required string? ScopePath { get; init; }
    public required SearchInvalidReason? InvalidReason { get; init; }
    // Single canonical hit list, one row per URL. Any block that resolves to a URL
    // shared with a page (or another block) folds into that URL's row via the
    // canonicaliser; the rendered result is always a list of URLs, never a mix of
    // page hits and standalone block hits.
    public required IReadOnlyList<CanonicalSearchHit> Hits { get; init; }
    public required int TotalCount { get; init; }
    // Zero-indexed for internal math (matches the shared _Pagination view model). URLs
    // are one-indexed at the widget boundary (see SiteSearchQuery.Page).
    public required int Page { get; init; }
    public required int PageSize { get; init; }
    public int TotalPages => PageSize > 0 && TotalCount > 0
        ? (TotalCount + PageSize - 1) / PageSize
        : 0;
}
