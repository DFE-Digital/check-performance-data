using DfE.CheckPerformanceData.Application.Analytics;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels;

// Landing-page view model for the /admin/Search/ dashboard. Carries the 4-tile summary,
// the two top-N tables, the four bucketed chart series (one per tile), the top-10 pages
// summary card, the resolved window and its label (echoed on the filter form so the
// currently-selected chip re-renders selected), and TotalRowCount so the view can render
// a small-sample or empty-window hint AFTER the data without re-reading the aggregate.
public sealed class SearchAnalyticsIndexViewModel
{
    public required SearchAnalyticsSummary Summary { get; init; }
    public required IReadOnlyList<TopQueryRow> TopQueries { get; init; }
    public required IReadOnlyList<TopQueryRow> TopZeroResultQueries { get; init; }
    public required IReadOnlyList<VolumeBucket> VolumeSeries { get; init; }
    public required DateTime FromUtc { get; init; }
    public required DateTime ToUtc { get; init; }
    public required string RangeKey { get; init; }
    public required int TotalRowCount { get; init; }

    // The bucket size the chart is being rendered at. One of "15m", "1h", "1d", "1w", "1mo".
    // Echoed back into the bucket-size selector so the currently-picked size stays checked
    // after a filter re-submission. Defaults to whatever the controller auto-picked from the
    // window width when no explicit ?bucket= was supplied.
    public required string BucketKey { get; init; }

    // Distinct sessions per bucket over the window. Feeds the "Unique users" chart shown
    // when the second tile is clicked. Value is in VolumeBucket.SearchCount.
    public required IReadOnlyList<VolumeBucket> UniqueSessionsSeries { get; init; }

    // Zero-result event count per bucket over the window. Feeds the "Zero-result rate"
    // chart shown when the third tile is clicked. Value is in VolumeBucket.SearchCount.
    public required IReadOnlyList<VolumeBucket> ZeroResultCountSeries { get; init; }

    // Latency percentiles (p5 / p50 / p95) per bucket over the window. Feeds the "P95
    // latency" chart shown when the fourth tile is clicked.
    public required IReadOnlyList<LatencyBucket> LatencyPercentileSeries { get; init; }

    // Top 10 pages by search impressions in the window. Rendered as an inline
    // govuk-summary-card alongside the top-queries and top-zero-result cards so admins
    // no longer have to click through to see which pages the search engine is surfacing.
    public required IReadOnlyList<TopPageRow> TopPages { get; init; }

    // Total distinct-page count in the window. When > TopPages.Count the view renders a
    // "View all top pages by search impressions →" link below the card that lands on
    // /admin/Search/Pages.
    public required int TopPagesTotalCount { get; init; }
}
