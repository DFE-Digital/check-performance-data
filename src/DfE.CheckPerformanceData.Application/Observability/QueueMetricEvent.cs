namespace DfE.CheckPerformanceData.Application.Observability;

// The write-side payload handed to an IMetricsSink whenever a consumer finishes with a
// message. Kept as a small, transport-agnostic record so an alternative sink (App Insights,
// a log appender) can be added later without touching the consumers. The queue work tables
// are deleted on ack, so a row built from this record is the only durable trace of the
// message having passed through a stage.
public sealed record QueueMetricEvent(
    string QueueName,
    string Stage,
    string ReferenceNumber,
    Guid MessageId,
    string? DecisionStatus,
    string? RulesVersion,
    double LatencyMs,
    DateTime RecordedAtUtc);
