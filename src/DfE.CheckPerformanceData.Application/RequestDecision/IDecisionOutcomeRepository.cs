using DfE.CheckPerformanceData.Application.RulesEngine;

namespace DfE.CheckPerformanceData.Application.RequestDecision;

/// <summary>
/// Writes the rules engine's decision back to the <c>ChangeRequests</c> row the
/// request was submitted from. The update is idempotent so a retried queue
/// message simply rewrites the same values.
/// </summary>
public interface IDecisionOutcomeRepository
{
    /// <returns><c>true</c> when a <c>ChangeRequests</c> row was updated.</returns>
    Task<bool> RecordOutcomeAsync(Guid changeRequestId, Decision decision, CancellationToken token);
}
