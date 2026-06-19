namespace DfE.CheckPerformanceData.Persistence.Entities;

// A poison message moved off a queue after exceeding its max attempts. Retains the original
// payload, a truncated failure reason, a payload hash and the attempt count so an operator can
// inspect, redrive or purge it. Never the result of a silent delete.
public sealed class DeadLetterEntity
{
    public Guid Id { get; set; }
    public string QueueName { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string PayloadHash { get; set; } = string.Empty;
    public int Attempts { get; set; }
    public DateTime EnqueuedAtUtc { get; set; }
    public DateTime DeadLetteredAtUtc { get; set; }
}
