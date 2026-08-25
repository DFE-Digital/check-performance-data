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

    // The landing dashboard shows the aggregate-to-typical-week checkbox alongside the
    // filter controls so a click on Apply filters carries the current aggregate state
    // (without this the checkbox would sit in its own form and get clobbered on every
    // filter submit). Drill-in callers leave both defaults - the checkbox is hidden
    // there and no aggregate value is submitted.
    public bool ShowAggregateToggle { get; init; }
    public bool AggregateOn { get; init; }
}
