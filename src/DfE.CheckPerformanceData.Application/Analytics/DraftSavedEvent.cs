namespace DfE.CheckPerformanceData.Application.Analytics;

/// <summary>
/// A draft change request was saved (after <c>SaveDraftAsync</c>). <c>Status</c> is the
/// resulting <c>RequestStatus</c> (ReadyToSubmit / InProgress). The reference number is
/// hidden (masked) pending the DPIA classification.
/// </summary>
public sealed record DraftSavedEvent : AnalyticsEvent
{
    public required string Status { get; init; }
    public required string WhatToChange { get; init; }
    public required string CheckingWindowType { get; init; }
    public required string ReferenceNumber { get; init; }

    public override string EventType => "draft_saved";

    public override IReadOnlyList<AnalyticsField> Fields =>
    [
        new("status", Status),
        new("what_to_change", WhatToChange),
        new("checking_window_type", CheckingWindowType),
        new("reference_number", ReferenceNumber, Hidden: true),
    ];
}
