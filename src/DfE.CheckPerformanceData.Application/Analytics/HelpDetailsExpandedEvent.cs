namespace DfE.CheckPerformanceData.Application.Analytics;

/// <summary>
/// Emitted when a user expands a details/summary help toggle (AB#286387 R18).
/// page_path is the referring page's path only — never its query string.
/// expand_text is client-supplied free text and is truncated server-side before
/// this record is constructed — never trust client input length.
/// </summary>
public sealed record HelpDetailsExpandedEvent : AnalyticsEvent
{
    public override string EventType => "help_details_expanded";

    public string? ExpandText { get; init; }

    public string? PagePath { get; init; }

    public override IReadOnlyList<AnalyticsField> Fields =>
    [
        new("expand_text", ExpandText),
        new("page_path", PagePath),
    ];
}
