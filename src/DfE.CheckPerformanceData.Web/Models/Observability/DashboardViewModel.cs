using DfE.CheckPerformanceData.Application.Observability;

namespace DfE.CheckPerformanceData.Web.Models.Observability;

// The server-rendered dashboard payload. It carries everything the page needs to render in its
// reading order without any client round-trip: the per-queue and overall health lights, the
// plain-English status sentence, the big-number tiles, and the chart series (each paired with a
// data table in the view). The live SSE stream refreshes a subset of this once the page is up.
public sealed class DashboardViewModel
{
    public required IReadOnlyList<QueueHealth> QueueHealth { get; init; }
    public required HealthState OverallHealth { get; init; }

    // Why the overall light is not flowing — the reasons of the unhappiest queue, so the headline
    // light explains itself just as the per-queue lights do. Empty when everything is flowing.
    public IReadOnlyList<HealthReason> OverallReasons { get; init; } = Array.Empty<HealthReason>();

    public required string StatusSentence { get; init; }

    public int ProcessedToday { get; init; }
    public TimeSpan TypicalEndToEnd { get; init; }
    public IReadOnlyList<QueueDepthSnapshot> Depths { get; init; } = Array.Empty<QueueDepthSnapshot>();

    public IReadOnlyList<ThroughputBucket> Throughput { get; init; } = Array.Empty<ThroughputBucket>();
    public IReadOnlyList<DecisionMixEntry> DecisionMix { get; init; } = Array.Empty<DecisionMixEntry>();
    public IReadOnlyList<DecisionMixBucket> DecisionMixOverTime { get; init; } = Array.Empty<DecisionMixBucket>();
    public IReadOnlyList<StageDwell> Dwell { get; init; } = Array.Empty<StageDwell>();
    public IReadOnlyList<DeployMarker> DeployMarkers { get; init; } = Array.Empty<DeployMarker>();

    // The resolved window/bucket selection the chart series were queried with, plus the
    // pairings the form may offer for the selected window. The headline tiles and status
    // sentence above the charts always describe the last 24 hours regardless of these.
    public string SelectedRange { get; init; } = DashboardRanges.DefaultValue;
    public ThroughputGranularity SelectedGranularity { get; init; } = ThroughputGranularity.Hour;
    public string RangeLabel { get; init; } = "Last 24 hours";
    public string GranularityLabel { get; init; } = "per hour";
    public IReadOnlyList<ThroughputGranularity> GranularityOptions { get; init; } =
        Array.Empty<ThroughputGranularity>();

    public DateTime RefreshedAtUtc { get; init; }

    // Whether the dev/test-only Demo panel renders: drive / inject / seed / replay / demo-trickle
    // controls folded in from the retired /dev/uat page. The dashboard itself is always-on admin;
    // only this panel is gated, on Dev:ToolsEnabled AND not-production, resolved by the controller.
    public bool DemoToolsEnabled { get; init; }
}

// One queue's resolved health light for the strip: the display name, the evaluated state, and the
// reasons behind a non-flowing state (which signals crossed which thresholds, with actual-vs-limit
// figures) so "needs attention" can explain itself. Reasons is empty for a flowing queue.
public sealed record QueueHealth(
    string QueueName,
    string DisplayName,
    HealthState State,
    IReadOnlyList<HealthReason> Reasons)
{
    public QueueHealth(string queueName, string displayName, HealthState state)
        : this(queueName, displayName, state, Array.Empty<HealthReason>())
    {
    }
}

// The geometry a time-axis chart hands the shared deploy-marker partial so the dashed
// rules-version verticals land on that chart's own plot area and time scale. One partial,
// many charts — the marker rendering is never duplicated per chart.
public sealed record ChartMarkersViewModel(
    IReadOnlyList<DeployMarker> Markers,
    DateTime? MinTime,
    DateTime? MaxTime,
    int PadLeft,
    int PadTop,
    int PlotWidth,
    int PlotHeight);

// The model the journey timeline partial binds to: the ordered stage events for one reference
// plus the reference itself. The "why was this decided?" slot is rendered empty by the view; no
// decision-detail data is carried here — rule-level decision explainability is owned elsewhere
// and intentionally not persisted by this surface.
public sealed class JourneyViewModel
{
    public required string ReferenceNumber { get; init; }
    public required IReadOnlyList<JourneyEvent> Events { get; init; }
}

// The click-to-inspect panel for one message on the board: its reference, the decision it
// reached and the per-stage queue status drawn from the recorded events. Journey-only by
// design: metrics are recorded after ack/dead-letter, so the queue row (and its payload) is
// gone by the time a token is on the board. Payload viewing stays on the queue admin surfaces,
// which carry the redaction and audit discipline.
public sealed class InspectViewModel
{
    public required string ReferenceNumber { get; init; }
    public string? Decision { get; init; }
    public required IReadOnlyList<JourneyEvent> Stages { get; init; }
}
