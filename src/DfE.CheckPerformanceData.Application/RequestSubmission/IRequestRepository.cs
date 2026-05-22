namespace DfE.CheckPerformanceData.Application.RequestSubmission;

public interface IRequestRepository
{
    Task<bool> IsSubmittedAsync(string referenceNumber);
    Task UpsertAsync(ChangeRequestData data);
}
