namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels;

// Envelope for the shared time-window + bucket-size filter partial. Same shape as the
// pieces the landing dashboard and the four series drill-ins both need: the currently-
// selected range key + from/to + bucket key, plus the form action URL to post back to.
// Kept tiny so any view that renders the filter can hand-build one without pulling in
// the full landing-page view model.
public sealed class TimeWindowFilterModel
{
    public required string FormAction { get; init; }
    public required string RangeKey { get; init; }
    public required DateTime FromUtc { get; init; }
    public required DateTime ToUtc { get; init; }
    public required string BucketKey { get; init; }
}
