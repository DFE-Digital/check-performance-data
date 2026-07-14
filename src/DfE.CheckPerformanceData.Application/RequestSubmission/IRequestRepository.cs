using DfE.CheckPerformanceData.Application.AmendmentRequests;

namespace DfE.CheckPerformanceData.Application.RequestSubmission;

public interface IRequestRepository
{
    Task<DuplicateCheckResult> CheckForConflictAsync(Guid windowId, Guid pupilId, long organisationUrn, string currentReferenceNumber, Guid currentUserId);

    /// <summary>Returns the reference number of a submitted request for the given pupil, or null if none exists.</summary>
    Task<string?> HasSubmittedRequestAsync(Guid windowId, Guid pupilId, long organisationUrn);
    /// <returns>The Id of the inserted or updated <c>ChangeRequests</c> row.</returns>
    Task<Guid> UpsertAsync(ChangeRequestData data);
    Task<IReadOnlyList<AmendmentRequestData>> GetAmendmentRequestsAsync(Guid windowId, long organisationUrn);
    Task<IReadOnlyList<SubmittedRequestData>> GetSubmittedRequestsAsync(Guid windowId, long organisationUrn);
    Task<AmendmentRequestData?> GetAmendmentRequestAsync(Guid windowId, long organisationUrn, string referenceNumber);
    Task<ConfirmDataCorrectData?> GetConfirmDataCorrectAsync(Guid windowId, long organisationUrn, string referenceNumber);

    /// <summary>
    /// Soft-deletes a request by setting its status to <see cref="Domain.Enums.RequestStatus.Withdrawn"/>.
    /// Scoped by window + org + reference so a school cannot withdraw another school's request.
    /// </summary>
    Task WithdrawAsync(Guid windowId, long organisationUrn, string referenceNumber);

    /// <summary>
    /// Hard-deletes a request row. Used for in-progress / ready-to-submit drafts.
    /// Scoped by window + org + reference so a school cannot delete another school's request.
    /// </summary>
    Task DeleteAsync(Guid windowId, long organisationUrn, string referenceNumber);
}
