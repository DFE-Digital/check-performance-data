using DfE.CheckPerformanceData.Application.Analytics;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels;

// Envelope for the reusable server-rendered scatter chart partial. Currently the only
// caller is the "Request timings" panel beneath the latency-percentiles chart on the
// admin dashboard, but the shape stays generic — a list of points, an optional sample
// hint, and an optional drill-in URL for the "open the paged view" link.
//
// SampledCount is how many points the reader actually returned; TotalCount is the raw
// event count in the window. When SampledCount < TotalCount the partial renders a
// "showing X of Y" note under the chart and a link to the DrillInUrl (skipped when the
// URL is null or empty).
public sealed class ScatterChartModel
{
    public required IReadOnlyList<RequestTimingPoint> Points { get; init; }
    public required int SampledCount { get; init; }
    public required int TotalCount { get; init; }
    public string? DrillInUrl { get; init; }
}
