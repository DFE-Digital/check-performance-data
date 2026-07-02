namespace DfE.CheckPerformanceData.Application.Analytics;

/// <summary>
/// An evidence file was removed from a journey. Counts only — never file names.
/// </summary>
public sealed record EvidenceFileRemovedEvent : AnalyticsEvent
{
    public required int FilesBefore { get; init; }
    public required int FilesAfter { get; init; }

    public override string EventType => "evidence_file_removed";

    public override IReadOnlyList<AnalyticsField> Fields =>
    [
        new("files_before", FilesBefore),
        new("files_after", FilesAfter),
    ];
}
