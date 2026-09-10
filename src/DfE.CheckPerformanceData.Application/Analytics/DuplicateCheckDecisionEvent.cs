namespace DfE.CheckPerformanceData.Application.Analytics;

/// <summary>
/// AB#297780: a decision on the Add journey's duplicate-check page. Deliberately PII-free: the
/// pupil identity is never serialized — only the match-count bucket, the action taken, and the
/// window type, so the team can tell how often the warning fires and what schools do about it
/// without any of the matched names or identifiers entering the analytics pipeline.
/// </summary>
public sealed record DuplicateCheckDecisionEvent : AnalyticsEvent
{
    /// <summary>One of None / SingleNonIncluded / SingleIncluded / Multiple.</summary>
    public required string Scenario { get; init; }

    /// <summary>shown | continue | abort (what happened on the page).</summary>
    public required string Action { get; init; }

    public required string CheckingWindowType { get; init; }

    public override string EventType => "duplicate_check_decision";

    public override IReadOnlyList<AnalyticsField> Fields =>
    [
        new("scenario", Scenario),
        new("action", Action),
        new("checking_window_type", CheckingWindowType),
    ];
}
