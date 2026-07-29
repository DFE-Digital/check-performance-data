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

    // A sampled slice of individual search events for the request-timings scatter chart
    // that renders BELOW the latency-percentiles chart when the latency tile is active.
    // Sampled to no more than ~2000 rows to keep the DOM tight; the caller renders a
    // "showing X of Y" hint underneath when RequestTimings.Count < RequestTimingsTotalCount.
    public required IReadOnlyList<RequestTimingPoint> RequestTimings { get; init; }

    // Total (unsampled) search event count in the current window. Drives the "sampled X of
    // Y" hint below the scatter and the visibility of the "open the paged view" link.
    public required int RequestTimingsTotalCount { get; init; }

    // 7 × 24 = 168 buckets, zero-filled, for the weekday × hour-of-day heatmap card.
    // Ordered by (weekday ascending, hour ascending) so the view iterates row by row —
    // one heatmap row per weekday.
    public required IReadOnlyList<WeekdayHourBucket> WeekdayHourGrid { get; init; }

    // Session-level rollup of "what happened after a zero-result search" for the funnel
    // card. All zeros is a valid state (rendered as an empty-state message).
    public required ZeroResultOutcomeSummary ZeroResultOutcomes { get; init; }

    // "Aggregate to a typical week" mode. When true, VolumeSeries / UniqueSessionsSeries /
    // ZeroResultCountSeries / LatencyPercentileSeries have been populated by the cyclic
    // weekday-hour aggregate readers (168 buckets each, one per hour of a typical week)
    // instead of the linear time-series readers. The view uses this flag to (a) render the
    // toggle checkbox in the checked state and (b) suppress the aggregate toggle from any
    // series drill-in link so a subsequent navigate lands on the raw-time view unless
    // explicitly asked for aggregate again.
    public required bool AggregateMode { get; init; }

    // Prior-window figures for the anomaly chips beneath each of the four stat tiles.
    // When Available == false the view hides every chip and renders an "insufficient
    // prior data" hint (a custom range spanning > 45 days puts the prior window outside
    // the sink's 90-day retention).
    public required SearchAnalyticsSummaryDeltas SummaryDeltas { get; init; }
}
