using DfE.CheckPerformanceData.Application.Analytics;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels;

// View models for the three admin drill-in tables. Each carries: the current page of rows,
// the total count for the window (drives pagination), the resolved page + pageSize (so the
// pagination widget knows current + total pages), and the resolved window + rangeKey (so
// links back to the landing page keep the same window and the "Back" link is contextual).
//
// The Queries + ZeroResults drill-ins share TopQueryRow — they differ only in the underlying
// SQL filter, not in the rendered row shape — so one view-model shape covers both. Pages has
// a distinct row shape (TopPageRow), so its own view-model.
//
// PageSize is trusted verbatim from CMS:PageLength (floor 1 only): admin editors set the
// value and drill-ins render it. No per-request URL override.

public sealed class SearchAnalyticsQueryDrillInViewModel
{
    public required IReadOnlyList<TopQueryRow> Rows { get; init; }
    public required int TotalCount { get; init; }
    public required int Page { get; init; }
    public required int PageSize { get; init; }
    public required DateTime FromUtc { get; init; }
    public required DateTime ToUtc { get; init; }
    public required string RangeKey { get; init; }

    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public sealed class SearchAnalyticsPagesDrillInViewModel
{
    public required IReadOnlyList<TopPageRow> Rows { get; init; }
    public required int TotalCount { get; init; }
    public required int Page { get; init; }
    public required int PageSize { get; init; }
    public required DateTime FromUtc { get; init; }
    public required DateTime ToUtc { get; init; }
    public required string RangeKey { get; init; }

    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}
