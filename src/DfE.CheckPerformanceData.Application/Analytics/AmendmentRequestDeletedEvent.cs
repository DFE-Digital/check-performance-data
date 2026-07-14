namespace DfE.CheckPerformanceData.Application.Analytics;

/// <summary>
/// The user deleted an amendment request (the successful POST on the delete
/// confirmation page). Drafts are hard-deleted; submitted requests are withdrawn —
/// <see cref="WasHardDeleted"/> distinguishes the two. The reference number is
/// hidden (masked) pending the DPIA classification.
/// </summary>
public sealed record AmendmentRequestDeletedEvent : AnalyticsEvent
{
    public required string ReferenceNumber { get; init; }
    public required bool WasHardDeleted { get; init; }

    public override string EventType => "amendment_request_deleted";

    public override IReadOnlyList<AnalyticsField> Fields =>
    [
        new("reference_number", ReferenceNumber, Hidden: true),
        new("was_hard_deleted", WasHardDeleted),
    ];
}
