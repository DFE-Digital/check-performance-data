namespace DfE.CheckPerformanceData.Application.RequestSubmission;

public interface IRequestRepository
{
    Task SaveAsync(RequestDocument document);
}
