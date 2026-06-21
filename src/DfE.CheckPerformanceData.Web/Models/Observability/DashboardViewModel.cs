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

    // Whether to render the per-queue health lights beneath the overall one. When every queue
    // light carries the same level and the same reasons as the overall (e.g. the only problem is a
    // shared dead-letter backlog that trips all of them identically), the per-queue blocks just
    // repeat the overall, so they are suppressed and only the overall block shows.
    public bool ShowPerQueueHealth { get; init; } = true;

    public required string StatusSentence { get; init; }

    public int ProcessedToday { get; init; }
    public TimeSpan TypicalEndToEnd { get; init; }

    // Ongoing 24-hour counts per decision, shown beside "processed today" so the headline reads
    // e.g. 103 processed → 62 auto-approved, 3 auto-rejected, 14 scrutiny, 1 dead-letter. The three
    // decision counts come from the 24-hour decision mix; the dead-letter count is the current DLQ
    // depth. The board engine keeps each live, incrementing the matching counter as a decided or
    // failed submission completes — like the processed-today tile.
    public int AutoApprovedToday { get; init; }
    public int AutoRejectedToday { get; init; }
    public int ScrutinyToday { get; init; }
    public int DeadLetterCount { get; init; }
    public IReadOnlyList<QueueDepthSnapshot> Depths { get; init; } = Array.Empty<QueueDepthSnapshot>();

    // The recent-submissions matrix, server-rendered so the table is populated on load rather than
    // empty until live traffic arrives: one row per recent reference with its per-stage timestamps,
    // decision, rules-engine latency and dead-letter flag. The board engine seeds its live grid from
    // these and folds in SSE updates on top.
    public IReadOnlyList<GroupedTransactionRow> RecentSubmissions { get; init; } =
        Array.Empty<GroupedTransactionRow>();

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

    // The custom-range from/to the charts were queried with, so the form's date inputs repopulate.
    // IsCustomRange drives whether those inputs are the active selection. The values are always
    // populated (even for a named range) so switching to "Custom" starts from the current window.
    public bool IsCustomRange { get; init; }
    public DateTime SelectedFromUtc { get; init; }
    public DateTime SelectedToUtc { get; init; }

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
