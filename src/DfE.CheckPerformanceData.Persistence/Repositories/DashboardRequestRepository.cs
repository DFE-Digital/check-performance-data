using DfE.CheckPerformanceData.Application.Dashboard;
using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Persistence.Repositories;

public sealed class DashboardRequestRepository(IPortalDbContext context) : IDashboardRequestRepository
{
    public async Task<DashboardRequestAggregates> GetRequestAggregatesAsync(
        Guid windowId, CancellationToken cancellationToken = default)
    {
        // Two columns per submitted request; request volumes per window are small enough
        // that in-memory counting beats four separate round trips.
        var rows = await context.ChangeRequests
            .Where(r => r.WindowId == windowId
                && (r.Status == RequestStatus.SubmittedUnCommitted
                    || r.Status == RequestStatus.SubmittedCommitted))
            .Select(r => new { r.OrganisationUrn, r.Outcome })
            .ToListAsync(cancellationToken);

        return new DashboardRequestAggregates
        {
            TotalRequests = rows.Count,
            AutoApproved = rows.Count(r => r.Outcome == DecisionStatus.AutoApproved),
            AutoRejected = rows.Count(r => r.Outcome == DecisionStatus.AutoRejected),
            RequiringScrutiny = rows.Count(r => r.Outcome == DecisionStatus.Scrutiny),
            SubmittingUrns = rows.Select(r => r.OrganisationUrn).Distinct().ToList(),
        };
    }
}
