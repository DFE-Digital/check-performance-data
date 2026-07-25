using DfE.CheckPerformanceData.Application.Search;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels;

public sealed class SiteSearchViewModel
{
    public required string Query { get; init; }
    public required string? Scope { get; init; }
    public required SearchInvalidReason? InvalidReason { get; init; }
    // Single URL-canonicalised hit list. The old PageHits + ContentBlockHits split is gone;
    // dedup + aggregation live in the canonicaliser and the view renders one merged list.
    // Hits is the CURRENT PAGE slice — use TotalCount for the corpus-wide result counter.
    public required IReadOnlyList<CanonicalSearchHit> Hits { get; init; }
    // Retained as pass-through so the URL query-string round-trips include-page /
    // include-block toggles unchanged. The view no longer renders separate corpus groups
    // so these values do not gate any visible section.
    public required bool IncludePages { get; init; }
    public required bool IncludeContentBlocks { get; init; }

    // Page is one-indexed at the URL boundary (?page=1 = first page); mirrors the widget's
    // shipped convention and round-trips cleanly through the paginator's link builder.
    public required int Page { get; init; }
    public required int PageSize { get; init; }
    // Canonical corpus total AFTER dedup — NOT the sliced Hits.Count. The "N results for X"
    // copy on the results page renders from this so power queries do not mis-report the
    // current-page slice as the whole corpus.
    public required int TotalCount { get; init; }

    // Convenience mirror kept so existing callers do not have to change spelling; both point
    // to the same canonical corpus figure.
    public int TotalHits => TotalCount;

    // Ceiling division on the corpus total feeds the paginator; zero-count / zero-pageSize
    // both fall through to zero pages so the paginator block stays hidden.
    public int TotalPages => PageSize > 0 && TotalCount > 0
        ? (TotalCount + PageSize - 1) / PageSize
        : 0;
}
