using DfE.CheckPerformanceData.Application.UncommittedRequests;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Persistence.Repositories;

public sealed class UncommittedRequestsRepository(IPortalDbContext db) : IUncommittedRequestsRepository
{
    public async Task<IReadOnlyList<UncommittedRequestRow>> GetForOpenWindowsAsync(
        DateTime now, CancellationToken cancellationToken)
    {
        var openWindowIds = await db.CheckingWindows
            .AsNoTracking()
            .Where(w => w.StartDate <= now && w.EndDate >= now)
            .Select(w => w.Id)
            .ToListAsync(cancellationToken);

        if (openWindowIds.Count == 0)
            return [];

        return await db.ChangeRequests
            .AsNoTracking()
            .Where(r => r.Status == RequestStatus.SubmittedUnCommitted
                && openWindowIds.Contains(r.WindowId))
            .OrderByDescending(r => r.Submitted)
            .Select(r => new UncommittedRequestRow
            {
                ReferenceNumber = r.ReferenceNumber,
                PupilFirstname = r.PupilFirstname,
                PupilSurname = r.PupilSurname,
                RequestTypeDescription = r.RequestTypeDescription,
                SubmittedByName = r.SubmittedByName,
                Submitted = r.Submitted,
                Outcome = r.Outcome,
                MatchedRule = r.MatchedRuleId,
                DecidedAtUtc = r.DecidedAtUtc
            })
            .ToListAsync(cancellationToken);
    }
}
