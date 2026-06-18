using DfE.CheckPerformanceData.Application.AmendmentRequests;

namespace DfE.CheckPerformanceData.Application.RequestSubmission;

public interface IRequestRepository
{
    Task<bool> HasConflictingRequestAsync(Guid windowId, string pupilUpn, long organisationUrn, string currentReferenceNumber);
    /// <returns>The Id of the inserted or updated <c>ChangeRequests</c> row.</returns>
    Task<Guid> UpsertAsync(ChangeRequestData data);
    Task<IReadOnlyList<AmendmentRequestData>> GetAmendmentRequestsAsync(Guid windowId, long organisationUrn);
    Task<IReadOnlyList<SubmittedRequestData>> GetSubmittedRequestsAsync(Guid windowId, long organisationUrn);
    Task<AmendmentRequestData?> GetAmendmentRequestAsync(Guid windowId, long organisationUrn, string referenceNumber);
    Task<ConfirmDataCorrectData?> GetConfirmDataCorrectAsync(Guid windowId, long organisationUrn, string referenceNumber);
}
