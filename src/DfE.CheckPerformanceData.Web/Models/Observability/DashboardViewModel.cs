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
    public required string StatusSentence { get; init; }

    public int ProcessedToday { get; init; }
    public TimeSpan TypicalEndToEnd { get; init; }
    public IReadOnlyList<QueueDepthSnapshot> Depths { get; init; } = Array.Empty<QueueDepthSnapshot>();

    public IReadOnlyList<ThroughputBucket> Throughput { get; init; } = Array.Empty<ThroughputBucket>();
    public IReadOnlyList<DecisionMixEntry> DecisionMix { get; init; } = Array.Empty<DecisionMixEntry>();
    public IReadOnlyList<StageDwell> Dwell { get; init; } = Array.Empty<StageDwell>();
    public IReadOnlyList<DeployMarker> DeployMarkers { get; init; } = Array.Empty<DeployMarker>();

    public DateTime RefreshedAtUtc { get; init; }
}

// One queue's resolved health light for the strip: the display name plus the evaluated state.
public sealed record QueueHealth(string QueueName, string DisplayName, HealthState State);

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
// reached, the per-stage queue status drawn from the recorded events, and a payload that is
// redacted by default. The full payload is only shown when the audited full-payload setting is
// on, mirroring the working-message detail discipline; if the message has already left the
// queues (ack deletes the row) no payload is available and only the journey is shown.
public sealed class InspectViewModel
{
    public required string ReferenceNumber { get; init; }
    public string? Decision { get; init; }
    public required IReadOnlyList<JourneyEvent> Stages { get; init; }
    public string Payload { get; init; } = string.Empty;
    public bool IsRedacted { get; init; }
    public bool PayloadAvailable { get; init; }
}
