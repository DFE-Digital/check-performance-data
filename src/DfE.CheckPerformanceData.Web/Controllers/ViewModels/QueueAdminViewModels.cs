using DfE.CheckPerformanceData.Application.Queue;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels;

public sealed class QueueIndexViewModel
{
    public required IReadOnlyList<QueueDepth> Queues { get; init; }
    public int DeadLetterCount { get; init; }
    public DateTime RefreshedAtUtc { get; init; }
}

public sealed class DlqListViewModel
{
    public required IReadOnlyList<DlqMessage> Messages { get; init; }
}

public sealed class DlqMessageViewModel
{
    public required Guid Id { get; init; }
    public required string QueueName { get; init; }
    public int Attempts { get; init; }
    public required string Reason { get; init; }
    public DateTime DeadLetteredAtUtc { get; init; }
    public required string Payload { get; init; }
    public bool IsRedacted { get; init; }
    public bool FullPayloadAvailable { get; init; }

    // The model is rendered to a string in the controller test to assert no raw
    // pupil identifiers leak; surfacing the payload here keeps that contract honest.
    public override string ToString() => Payload;
}
