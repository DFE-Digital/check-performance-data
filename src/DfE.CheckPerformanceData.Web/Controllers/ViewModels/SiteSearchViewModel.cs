using DfE.CheckPerformanceData.Application.Search;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels;

public sealed class SiteSearchViewModel
{
    public required string Query { get; init; }
    public required string? Scope { get; init; }
    public required SearchInvalidReason? InvalidReason { get; init; }
    // Single URL-canonicalised hit list. The old PageHits + ContentBlockHits split is gone;
    // dedup + aggregation live in the canonicaliser and the view renders one merged list.
    public required IReadOnlyList<CanonicalSearchHit> Hits { get; init; }
    // Retained as pass-through so the URL query-string round-trips include-page /
    // include-block toggles unchanged. The view no longer renders separate corpus groups
    // so these values do not gate any visible section.
    public required bool IncludePages { get; init; }
    public required bool IncludeContentBlocks { get; init; }
    public int TotalHits => Hits.Count;
}
