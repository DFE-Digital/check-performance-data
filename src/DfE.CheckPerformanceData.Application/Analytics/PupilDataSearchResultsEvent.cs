namespace DfE.CheckPerformanceData.Application.Analytics;

/// <summary>
/// A pupil search returned results. Emitted only when a search term was entered.
/// Carries the result count and which tab was searched — never the search term.
/// </summary>
public sealed record PupilDataSearchResultsEvent : AnalyticsEvent
{
    public required int ResultCount { get; init; }
    public required string ActiveTab { get; init; }

    public override string EventType => "pupil_data_search_results";

    public override IReadOnlyList<AnalyticsField> Fields =>
    [
        new("result_count", ResultCount),
        new("active_tab", ActiveTab),
    ];
}
