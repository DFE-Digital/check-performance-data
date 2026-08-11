namespace DfE.CheckPerformanceData.Application.Analytics;

/// <summary>
/// Emitted when a user selects a file in the evidence upload picker (AB#286387 R23).
/// page_path is the referring page's path only — never its query string.
/// </summary>
public sealed record EvidenceFileSelectedEvent : AnalyticsEvent
{
    public override string EventType => "evidence_file_selected";

    public string? PagePath { get; init; }

    public override IReadOnlyList<AnalyticsField> Fields =>
    [
        new("page_path", PagePath),
    ];
}
