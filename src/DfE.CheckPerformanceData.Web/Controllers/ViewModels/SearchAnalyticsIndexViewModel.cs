using DfE.CheckPerformanceData.Application.Analytics;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels;

// Landing-page view model for the /admin/Search/ dashboard. Carries the 4-tile summary,
// the two top-N tables, the resolved window and its label (echoed on the filter form so
// the currently-selected chip re-renders selected), and the empty-state guard
// (HasData = TotalRowCount >= 20). TotalRowCount is a separate field from Summary.TotalCount
// because the empty-state view still wants to say "we have N rows below the threshold" without
// re-running the aggregate.
public sealed class SearchAnalyticsIndexViewModel
{
    public required SearchAnalyticsSummary Summary { get; init; }
    public required IReadOnlyList<TopQueryRow> TopQueries { get; init; }
    public required IReadOnlyList<TopQueryRow> TopZeroResultQueries { get; init; }
    public required DateTime FromUtc { get; init; }
    public required DateTime ToUtc { get; init; }
    public required string RangeKey { get; init; }
    public required bool HasData { get; init; }
    public required int TotalRowCount { get; init; }
}
