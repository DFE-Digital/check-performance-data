namespace DfE.CheckPerformanceData.Application.Analytics;

/// <summary>
/// An evidence file upload was attempted. <c>Outcome</c> is <c>success</c> or
/// <c>failed</c>; <c>FailureReason</c> is a controlled taxonomy
/// (<c>no_file</c> / <c>too_large</c> / <c>not_a_pdf</c> / <c>page_limit_exceeded</c>)
/// and is null on success. No file name is ever included.
/// </summary>
public sealed record EvidenceUploadAttemptedEvent : AnalyticsEvent
{
    public required string Outcome { get; init; }
    public string? FailureReason { get; init; }
    public int? PageCount { get; init; }
    public long? FileSizeBytes { get; init; }

    public override string EventType => "evidence_upload_attempted";

    public override IReadOnlyList<AnalyticsField> Fields =>
    [
        new("outcome", Outcome),
        new("failure_reason", FailureReason),
        new("page_count", PageCount),
        new("file_size_bytes", FileSizeBytes),
    ];
}
