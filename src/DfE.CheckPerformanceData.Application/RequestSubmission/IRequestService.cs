using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.RequestSubmission;

public interface IRequestService
{
    /// <summary>Checks whether a submitted request already exists for the given pupil. Returns <see cref="DuplicateCheckResult"/> discriminating between no conflict, self-submitted, and other-submitted.</summary>
    Task<DuplicateCheckResult> HasSubmittedRequestAsync(Guid windowId, Guid pupilId, long organisationUrn);

    /// <summary>
    /// Submits a request without sending the confirmation email: conflict check, upsert the
    /// ChangeRequests row (SubmittedUnCommitted), enqueue the rules-engine document, and persist
    /// the journey blob. Throws <see cref="DuplicateRequestException"/> on a conflicting request.
    /// The email is the caller's responsibility (single path sends one; bulk path batches).
    /// </summary>
    Task SubmitRequestAsync(Guid windowId, RequestState journey);

    Task ConfirmRequestAsync(Guid windowId, RequestState journey);
    Task SaveDraftAsync(Guid windowId, RequestState journey, RequestStatus status);
    Task<RequestState?> ResumeDraftAsync(Guid windowId, string referenceNumber);
    Task ConfirmDataCorrectAsync(Guid windowId, string referenceNumber, DateTime endDate);

    /// <summary>
    /// Deletes a request, scoped to the current user's organisation. Drafts
    /// (InProgress / ReadyToSubmit) are hard-deleted (row + journey blob); submitted
    /// requests are soft-deleted by setting their status to <see cref="RequestStatus.Withdrawn"/>.
    /// </summary>
    Task<RequestDeletionResult> DeleteAsync(Guid windowId, string referenceNumber);
}
