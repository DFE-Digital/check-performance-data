namespace DfE.CheckPerformance.Persistence.Entities;

// An append-only record of a message passing through a queue stage. The queue work tables
// are deleted on ack, so this is the only durable home for throughput-over-time, per-stage
// dwell, the per-message journey timeline and board replay. The worker writes rows; the web
// process reads them, so it lives in the shared database. DecisionStatus is denormalised onto
// the row so the decision-mix-over-time chart needs no join and survives change-request
// lifecycle changes.
public sealed class QueueMetricEvent
{
    public long Id { get; set; }
    public string QueueName { get; set; } = string.Empty;
    public string Stage { get; set; } = string.Empty;
    public string ReferenceNumber { get; set; } = string.Empty;
    public Guid MessageId { get; set; }
    public string? DecisionStatus { get; set; }
    public string? RulesVersion { get; set; }
    public double LatencyMs { get; set; }
    public DateTime RecordedAtUtc { get; set; }

    // The instant the consumer dequeued this message and began processing it (null for Submitted,
    // an instantaneous enqueue). Lets the read side separate queue wait from processing time per
    // stage rather than reporting the whole enqueue→done span twice.
    public DateTime? StartedAtUtc { get; set; }
}
