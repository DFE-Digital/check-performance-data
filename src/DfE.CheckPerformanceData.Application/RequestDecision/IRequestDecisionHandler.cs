using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Application.RulesEngine;

namespace DfE.CheckPerformanceData.Application.RequestDecision;

public interface IRequestDecisionHandler
{
    Task HandleAsync(RequestDocument document, Decision decision, CancellationToken token);
}
