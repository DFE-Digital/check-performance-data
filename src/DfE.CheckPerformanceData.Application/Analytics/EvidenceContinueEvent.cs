namespace DfE.CheckPerformanceData.Application.Analytics;

/// <summary>
/// The user continued past an evidence page with valid input. Counts and the length
/// of any free-text reason — never the text itself, nor file names.
/// </summary>
public sealed record EvidenceContinueEvent : AnalyticsEvent
{
    public required int FileCount { get; init; }
    public required int PageCount { get; init; }
    public required int EvidenceTextLength { get; init; }

    public override string EventType => "evidence_continue";

    public override IReadOnlyList<AnalyticsField> Fields =>
    [
        new("file_count", FileCount),
        new("page_count", PageCount),
        new("evidence_text_length", EvidenceTextLength),
    ];
}
