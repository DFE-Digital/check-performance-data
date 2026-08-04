using DfE.CheckPerformanceData.Application.Analytics;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels;

// View model for the /admin/Search/RequestTimings drill-in. Renders one row per raw
// search event over the current window paged by CMS:PageLength. Session IDs render
// masked to their first 8 chars (readability) with the full value on hover via the
// title attribute. Reached from the "View all request timings →" link that appears
// below the scatter chart when the sample-cap is engaged on the landing dashboard.
public sealed class SearchAnalyticsRequestTimingsViewModel
{
    public required IReadOnlyList<RequestTimingPoint> Rows { get; init; }
    public required int TotalCount { get; init; }
    public required int Page { get; init; }
    public required int PageSize { get; init; }
    public required DateTime FromUtc { get; init; }
    public required DateTime ToUtc { get; init; }
    public required string RangeKey { get; init; }

    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}
