namespace DfE.CheckPerformanceData.Application.AmendmentRequests;

public interface IBulkSubmissionService
{
    /// <summary>
    /// Classifies the user's selected references (scoped to the current org, kept only if
    /// ReadyToSubmit) into submittable vs duplicate. A request is a duplicate if its pupil already
    /// has a submitted request, or if the pupil appears more than once in the selection (in which
    /// case all of that pupil's selected drafts are flagged and none are submittable).
    /// </summary>
    Task<BulkReviewResult> BuildReviewAsync(Guid windowId, IReadOnlyList<string> selectedReferences);

    /// <summary>
    /// Submits every submittable reference (best-effort; conflicts are skipped) and sends the
    /// batch email(s) per the consolidation threshold. Re-runs classification defensively.
    /// </summary>
    Task<BulkSubmissionResult> SubmitAsync(Guid windowId, IReadOnlyList<string> references);
}
