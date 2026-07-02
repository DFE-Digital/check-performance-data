namespace DfE.CheckPerformanceData.Application.Analytics;

/// <summary>
/// A change request was successfully submitted (after <c>ConfirmRequestAsync</c>).
/// The reference number is sent as hidden (masked) data pending the DPIA
/// classification — the hash still links the saved → resumed → submitted funnel.
/// </summary>
public sealed record RequestSubmittedEvent : AnalyticsEvent
{
    public required string WhatToChange { get; init; }
    public required string CheckingWindowType { get; init; }
    public required string ReferenceNumber { get; init; }

    public override string EventType => "request_submitted";

    public override IReadOnlyList<AnalyticsField> Fields =>
    [
        new("what_to_change", WhatToChange),
        new("checking_window_type", CheckingWindowType),
        new("reference_number", ReferenceNumber, Hidden: true),
    ];
}
