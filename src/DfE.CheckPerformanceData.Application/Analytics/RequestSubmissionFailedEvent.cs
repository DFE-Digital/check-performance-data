namespace DfE.CheckPerformanceData.Application.Analytics;

/// <summary>
/// A change request submission was rejected before it could be recorded — currently
/// only the duplicate/conflict case (<c>failure_reason = "duplicate_request"</c>).
/// </summary>
public sealed record RequestSubmissionFailedEvent : AnalyticsEvent
{
    public required string FailureReason { get; init; }
    public required string WhatToChange { get; init; }
    public required string CheckingWindowType { get; init; }

    public override string EventType => "request_submission_failed";

    public override IReadOnlyList<AnalyticsField> Fields =>
    [
        new("failure_reason", FailureReason),
        new("what_to_change", WhatToChange),
        new("checking_window_type", CheckingWindowType),
    ];
}
