using DfE.CheckPerformanceData.Application.RequestSubmission;


namespace DfE.CheckPerformanceData.Application.RequestDecision;

public interface IRequestDecisionHandler
{
    Task HandleAsync(RequestDocument message, CancellationToken token);
}