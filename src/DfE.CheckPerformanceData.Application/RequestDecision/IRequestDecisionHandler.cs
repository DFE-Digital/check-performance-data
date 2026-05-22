using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Domain.QueueMessages;

namespace DfE.CheckPerformanceData.Application.RequestDecision;

public interface IRequestDecisionHandler
{
    Task HandleAsync(RequestMessage message, Decision decision, CancellationToken token);
}
