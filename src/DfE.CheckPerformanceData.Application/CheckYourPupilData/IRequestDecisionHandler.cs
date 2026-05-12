using DfE.CheckPerformanceData.Domain.QueueMessages;

namespace DfE.CheckPerformanceData.Application.CheckYourPupilData;

public interface IRequestDecisionHandler
{
    Task HandleAsync(RequestMessage message, CancellationToken token);
}