namespace DfE.CheckPerformanceData.Application.Analytics;

/// <summary>
/// Emitted after a site/guidance search runs, carrying the result total so
/// zero-result searches are visible in BigQuery (AB#286387 R22).
/// The search term itself is intentionally not a field — it is already
/// visible in web_request.request_query for /search (accepted in QA, R21).
/// </summary>
public sealed record SearchResultCountEvent : AnalyticsEvent
{
    public override string EventType => "search_result_count";

    public required int ResultCount { get; init; }

    public string? Scope { get; init; }

    public override IReadOnlyList<AnalyticsField> Fields =>
    [
        new("result_count", ResultCount),
        new("scope", Scope),
    ];
}
