namespace DfE.CheckPerformanceData.Application.Analytics;

/// <summary>
/// A saved draft was resumed back into the journey (the second leg of the
/// saved → resumed → submitted funnel). The reference number is hidden (masked)
/// pending the DPIA classification; its hash links the funnel steps.
/// </summary>
public sealed record DraftResumedEvent : AnalyticsEvent
{
    public required string ReferenceNumber { get; init; }
    public required string WhatToChange { get; init; }
    public required string CheckingWindowType { get; init; }

    public override string EventType => "draft_resumed";

    public override IReadOnlyList<AnalyticsField> Fields =>
    [
        new("reference_number", ReferenceNumber, Hidden: true),
        new("what_to_change", WhatToChange),
        new("checking_window_type", CheckingWindowType),
    ];
}
