namespace DfE.CheckPerformanceData.Application.RequestSubmission;

public interface IRequestRepository
{
    Task<bool> ExistsAsync(string referenceNumber);
    Task SaveAsync(RequestDocument document);
}
