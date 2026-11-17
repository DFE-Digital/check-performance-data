using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.Notify;
using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.RequestSubmission;

public interface IRequestService
{
    /// <summary>Checks whether a submitted request already exists for the given pupil. Returns <see cref="DuplicateCheckResult"/> discriminating between no conflict, self-submitted, and other-submitted.</summary>
    Task<DuplicateCheckResult> HasSubmittedRequestAsync(Guid windowId, Guid pupilId, long organisationUrn);

    /// <summary>
    /// Submits a request without sending the confirmation email: conflict check, upsert the
    /// ChangeRequests row (Submitted), enqueue the rules-engine document, and persist
    /// the journey blob. Throws <see cref="DuplicateRequestException"/> on a conflicting request.
    /// The email is the caller's responsibility (single path sends one; bulk path batches).
    /// </summary>
    Task SubmitRequestAsync(Guid windowId, RequestState journey);

    /// <summary>
    /// Submits a 16-19 results enquiry and returns its reference number. Writes the
    /// <see cref="Domain.Enums.RequestType.ResultsEnquiry"/> row with the enquiry's subject, saves the
    /// journey JSON, and queues the enquiry for Zendesk once. The confirmation email is the caller's
    /// responsibility.
    /// </summary>
    /// <exception cref="DuplicateEnquiryException">The school already has a submitted enquiry for the
    /// same student and subject. Nothing is saved or queued.</exception>
    /// <exception cref="InvalidOperationException">The journey is not a results enquiry, or is incomplete.</exception>
    Task<string> SubmitResultsEnquiryAsync(Guid windowId, RequestState journey, CancellationToken ct = default);

    /// <summary>
    /// Returns the reference number of a results enquiry that the signed-in school has already
    /// submitted in the window for the journey's student and <paramref name="subject"/>, or null when
    /// there is none. The journey's own reference and a withdrawn enquiry do not count. Returns null
    /// when the journey has no student.
    /// </summary>
    Task<string?> FindDuplicateResultsEnquiryAsync(
        Guid windowId, RequestState journey, EnquirySubject subject, CancellationToken ct = default);

    Task ConfirmRequestAsync(Guid windowId, RequestState journey);
    Task SaveDraftAsync(Guid windowId, RequestState journey, RequestStatus status);
    Task<RequestState?> ResumeDraftAsync(Guid windowId, string referenceNumber);
    Task ConfirmDataCorrectAsync(Guid windowId, string referenceNumber, DateTime endDate, EmailSubstitutions substitutions);

    /// <summary>
    /// Deletes a request, scoped to the current user's organisation. Drafts
    /// (InProgress / ReadyToSubmit) are hard-deleted (row + journey blob); submitted
    /// requests are soft-deleted by setting their status to <see cref="RequestStatus.Withdrawn"/>.
    /// </summary>
    Task<RequestDeletionResult> DeleteAsync(Guid windowId, string referenceNumber);
}
