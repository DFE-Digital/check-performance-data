namespace DfE.CheckPerformanceData.Application.Analytics;

/// <summary>
/// Emitted when the phase-banner feedback link is clicked (AB#286387 R20).
/// page_path is the referring page's path only — never its query string.
/// </summary>
public sealed record FeedbackClickedEvent : AnalyticsEvent
{
    public override string EventType => "feedback_clicked";

    public string? PagePath { get; init; }

    public override IReadOnlyList<AnalyticsField> Fields =>
    [
        new("page_path", PagePath),
    ];
}
