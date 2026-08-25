using DfE.CheckPerformanceData.Application.Analytics;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels;

// View model for the four time-series drill-in pages under /admin/Search/{Volume,
// UniqueUsers,ZeroResultsOverTime,LatencyOverTime}. Each page renders the SAME chart
// as the landing dashboard (via one of the three chart partials) at the top, then a
// paged table of buckets beneath. The drill-in exists so admins can see the full time
// series when the landing-page inline data-table would be too long — the inline
// fallback under the chart on /admin/Search/ caps at 20 rows and links out here.
//
// SeriesKey is one of "volume" / "unique-users" / "zero-results" / "latency" so the
// view can select the right chart partial + column headings + pager URL. Rows is the
// paged slice; ChartRows is the full (unpaged) series for the chart at the top. Total
// bucket count comes back with the paged read so pagination is one call.
public sealed class SearchAnalyticsSeriesDrillInViewModel
{
    // One of: "volume" | "unique-users" | "zero-results" | "latency".
    public required string SeriesKey { get; init; }

    // Page 1 = header + intro; subsequent pages show the next slice of buckets.
    public required int Page { get; init; }
    public required int PageSize { get; init; }
    public required int TotalCount { get; init; }

    // Window + bucket-size the user picked, mirrored back into the filter form so the
    // currently-selected radio stays checked after Apply.
    public required DateTime FromUtc { get; init; }
    public required DateTime ToUtc { get; init; }
    public required string RangeKey { get; init; }
    public required string BucketKey { get; init; }

    // Full unpaged series for the chart at the top of the page. Kept as the full series
    // (not the paged slice) so the chart shows the same trend the landing card shows.
    public required IReadOnlyList<VolumeBucket> ChartVolumeSeries { get; init; }
    public required IReadOnlyList<LatencyBucket> ChartLatencySeries { get; init; }

    // Paged slice for the table below the chart. Only one of these two lists is populated
    // per request — the other is empty. VolumeSlice covers volume / unique-users /
    // zero-results (VolumeBucket shape); LatencySlice covers latency (LatencyBucket shape).
    public required IReadOnlyList<VolumeBucket> VolumeSlice { get; init; }
    public required IReadOnlyList<LatencyBucket> LatencySlice { get; init; }

    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}
