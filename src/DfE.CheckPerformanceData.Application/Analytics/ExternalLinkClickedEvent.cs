namespace DfE.CheckPerformanceData.Application.Analytics;

/// <summary>
/// Emitted when a user clicks a link that leaves the service (AB#286387 R19).
/// page_path is the referring page's path only — never its query string.
/// destination is a normalised, allowlist-mapped identifier — never a raw URL.
/// </summary>
public sealed record ExternalLinkClickedEvent : AnalyticsEvent
{
    public override string EventType => "external_link_clicked";

    public required string Destination { get; init; }

    public string? PagePath { get; init; }

    public override IReadOnlyList<AnalyticsField> Fields =>
    [
        new("destination", Destination),
        new("page_path", PagePath),
    ];
}
