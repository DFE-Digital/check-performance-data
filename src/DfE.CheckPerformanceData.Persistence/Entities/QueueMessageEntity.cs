namespace DfE.CheckPerformanceData.Persistence.Entities;

// A single durable queue row. Claimed by a consumer via FOR UPDATE SKIP LOCKED, which sets
// VisibleAfterUtc forward by the visibility timeout and increments Attempts. An unacked row
// becomes claimable again once VisibleAfterUtc has passed (crashed-worker recovery).
public sealed class QueueMessageEntity
{
    public Guid Id { get; set; }
    public string QueueName { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public int Attempts { get; set; }
    public DateTime EnqueuedAtUtc { get; set; }
    public DateTime VisibleAfterUtc { get; set; }
    public string Status { get; set; } = string.Empty;
}
