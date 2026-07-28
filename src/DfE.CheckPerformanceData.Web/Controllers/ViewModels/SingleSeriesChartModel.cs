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
}
