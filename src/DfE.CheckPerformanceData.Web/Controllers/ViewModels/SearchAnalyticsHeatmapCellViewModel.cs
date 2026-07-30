using DfE.CheckPerformanceData.Application.Analytics;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels;

// View model for the heatmap-cell drill-in reached from clicking a cell on the "When
// people search" grid. Weekday is ISO 8601 (1 = Monday, 7 = Sunday). Hour is 0..23 in
// UTC. WeekdayLabel + HourLabel are pre-rendered for the view so the heatmap partial
// and the drill-in header stay in lockstep. The rest mirrors the RequestTimings
// drill-in shape so the two paged surfaces share layout conventions.
public sealed class SearchAnalyticsHeatmapCellViewModel
{
    public required IReadOnlyList<RequestTimingPoint> Rows { get; init; }
    public required int TotalCount { get; init; }
    public required int Page { get; init; }
    public required int PageSize { get; init; }
    public required DateTime FromUtc { get; init; }
    public required DateTime ToUtc { get; init; }
    public required string RangeKey { get; init; }

    public required int Weekday { get; init; }
    public required int Hour { get; init; }
    public required string WeekdayLabel { get; init; }
    public required string HourLabel { get; init; }

    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}
