namespace DfE.CheckPerformanceData.Application.Queue;

public enum QueueMessageStatus
{
    Pending,
    InFlight,
    Acknowledged,
    DeadLettered
}

public sealed class QueueMessage
{
    public required Guid Id { get; init; }
    public required string QueueName { get; init; }
    public required string Payload { get; init; }
    public int Attempts { get; init; }
    public DateTime EnqueuedAt { get; init; }
    public DateTime VisibleAfterUtc { get; init; }
    public QueueMessageStatus Status { get; init; }
}
