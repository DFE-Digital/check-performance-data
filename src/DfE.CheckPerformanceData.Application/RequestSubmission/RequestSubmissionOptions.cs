namespace DfE.CheckPerformanceData.Application.RequestSubmission;

/// <summary>
/// Controls how a submitted <see cref="RequestDocument"/> is dispatched.
/// TEMPORARY: while the rules-engine queue path is paused, setting
/// <see cref="WriteToBlobInsteadOfQueue"/> makes submissions write the document to
/// blob storage (<see cref="IRequestBlobClient"/>) instead of enqueuing it
/// (<see cref="IRequestQueueClient"/>). Remove this switch once queueing is restored.
/// </summary>
public sealed class RequestSubmissionOptions
{
    public const string SectionName = "RequestSubmission";

    public bool WriteToBlobInsteadOfQueue { get; set; }
}
