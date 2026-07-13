using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.RequestSubmission;

public interface IRequestService
{
    /// <summary>Returns the reference number of a submitted request for the given pupil, or null if none exists.</summary>
    Task<string?> HasSubmittedRequestAsync(Guid windowId, Guid pupilId, long organisationUrn);

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
