using DfE.CheckPerformanceData.Application.Queue;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels;

public sealed class QueueIndexViewModel
{
    // One panel per queue (rules-engine, zendesk, dead-letter), each rendered through the same
    // reused partial: count + oldest-age + top-5 + view-all link.
    public required IReadOnlyList<QueuePanelViewModel> Panels { get; init; }
    public DateTime RefreshedAtUtc { get; init; }
}

// A single queue's panel data. The dead-letter queue is modelled here too (IsDeadLetter) so the
// same partial renders all three; its actions point at the existing DLQ surface rather than the
// working-queue view-all.
public sealed class QueuePanelViewModel
{
    public required string QueueName { get; init; }
    public required string DisplayName { get; init; }
    public int Count { get; init; }
    public TimeSpan? OldestMessageAge { get; init; }
    public IReadOnlyList<QueueMessageSummary> TopMessages { get; init; } = Array.Empty<QueueMessageSummary>();
    public bool IsDeadLetter { get; init; }
}

public sealed class QueueListViewModel
{
    public required string QueueName { get; init; }
    public required string DisplayName { get; init; }
    public required IReadOnlyList<QueueMessageSummary> Messages { get; init; }
}

public sealed class QueueMessageViewModel
{
    public required Guid Id { get; init; }
    public required string QueueName { get; init; }
    public int Attempts { get; init; }
    public DateTime EnqueuedAtUtc { get; init; }
    public required string Payload { get; init; }
    public bool IsRedacted { get; init; }
    public bool FullPayloadAvailable { get; init; }

    // The request reference extracted from the payload (null when unparseable), used to link
    // the message to its journey timeline. References are the redaction-safe key the
    // observability surfaces already display — never a pupil identifier.
    public string? ReferenceNumber { get; init; }

    public override string ToString() => Payload;
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

    // The request reference extracted from the payload (null when unparseable), used to link
    // the message to its journey timeline.
    public string? ReferenceNumber { get; init; }

    // The model is rendered to a string in the controller test to assert no raw
    // pupil identifiers leak; surfacing the payload here keeps that contract honest.
    public override string ToString() => Payload;
}
