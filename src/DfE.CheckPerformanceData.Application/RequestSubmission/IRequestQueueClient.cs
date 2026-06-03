namespace DfE.CheckPerformanceData.Application.RequestSubmission;

public interface IRequestQueueClient
{
    Task EnqueueRequestAsync(RequestDocument document);
}