using DfE.CheckPerformanceData.Application.Analytics;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels;

// Envelope for the single-series line chart partial. Same VolumeBucket shape as the
// dual-axis volume chart, but only one series is drawn — the value in each bucket lives
// in VolumeBucket.SearchCount. Title + Y-axis label + line colour + unit suffix vary
// per tile so the partial can render either "Unique users over time" or "Zero-result
// events over time" from the same template.
public sealed class SingleSeriesChartModel
{
    public required IReadOnlyList<VolumeBucket> Series { get; init; }
    public required string Title { get; init; }
    public required string YAxisLabel { get; init; }
    public required string SeriesColour { get; init; }

    // Suffix appended to peak-value copy in the SVG's aria-label — e.g. " ms" or empty.
    // Kept optional so the two current callers can both go without it (unit is carried
    // by the axis label instead).
    public string ValueUnit { get; init; } = string.Empty;

    // Where the inline fallback table's "View all …" link points when the total bucket
    // count exceeds the inline row cap. Optional — when null the fallback table renders
    // uncapped without an outbound link (behaviour on the drill-in page itself, where
    // the paged table has its own pager).
    public string? DrillInUrl { get; init; }

    // The label the "View all …" link uses. Kept parameterised so each caller's link text
    // matches the chart's own subject (e.g. "View all unique-sessions data").
    public string DrillInLinkText { get; init; } = "View all data";
}
