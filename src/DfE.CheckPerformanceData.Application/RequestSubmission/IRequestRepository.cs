using DfE.CheckPerformanceData.Application.AmendmentRequests;

namespace DfE.CheckPerformanceData.Application.RequestSubmission;

public interface IRequestRepository
{
    Task<bool> HasConflictingRequestAsync(Guid windowId, string pupilUpn, long organisationUrn, string currentReferenceNumber);
    Task UpsertAsync(ChangeRequestData data);
    Task<IReadOnlyList<AmendmentRequestData>> GetAmendmentRequestsAsync(Guid windowId, long organisationUrn);
}
