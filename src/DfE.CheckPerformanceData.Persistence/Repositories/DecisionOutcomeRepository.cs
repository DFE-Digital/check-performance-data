using DfE.CheckPerformanceData.Application.RequestDecision;
using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Persistence.Repositories;

public sealed class DecisionOutcomeRepository(IPortalDbContext db) : IDecisionOutcomeRepository
{
    public async Task<bool> RecordOutcomeAsync(Guid changeRequestId, Decision decision, CancellationToken token)
    {
        var updated = await db.ChangeRequests
            .Where(r => r.Id == changeRequestId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Outcome, decision.Status)
                .SetProperty(r => r.OutcomeKey, decision.OutcomeKey)
                .SetProperty(r => r.MatchedRuleId, decision.MatchedRuleId), token);

        return updated > 0;
    }
}
