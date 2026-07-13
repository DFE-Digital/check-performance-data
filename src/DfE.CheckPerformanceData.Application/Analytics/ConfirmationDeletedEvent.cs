namespace DfE.CheckPerformanceData.Application.Analytics;

/// <summary>
/// The user deleted their "pupil data is correct" confirmation (the successful POST on
/// the delete confirmation page for a ConfirmCorrect request). Confirmations are only
/// ever submitted, so the deletion is always a withdrawal. The reference number is
/// hidden (masked) pending the DPIA classification.
/// </summary>
public sealed record ConfirmationDeletedEvent : AnalyticsEvent
{
    public required string ReferenceNumber { get; init; }

    public override string EventType => "confirmation_deleted";

    public override IReadOnlyList<AnalyticsField> Fields =>
    [
        new("reference_number", ReferenceNumber, Hidden: true),
    ];
}
