namespace DfE.CheckPerformanceData.Application.RequestSubmission;

public interface IRequestRepository
{
    Task<bool> HasConflictingRequestAsync(Guid windowId, string pupilUpn, long organisationUrn, string currentReferenceNumber);
    Task UpsertAsync(ChangeRequestData data);
}
