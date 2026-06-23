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
            .Where(r => openWindowIds.Contains(r.WindowId))
            .OrderByDescending(r => r.Submitted)
            .Select(r => new UncommittedRequestRow
            {
                ReferenceNumber = r.ReferenceNumber,
                OrganisationUrn = r.OrganisationUrn,
                PupilFirstname = r.PupilFirstname,
                PupilSurname = r.PupilSurname,
                RequestTypeDescription = r.RequestTypeDescription,
                Status = r.Status,
                SubmittedByName = r.SubmittedByName,
                Submitted = r.Submitted,
                Outcome = r.Outcome,
                MatchedRule = r.MatchedRuleId,
                DecidedAtUtc = r.DecidedAtUtc,
                CrmId = r.CrmId
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ReplayRequestRow>> GetRequestsForOpenWindowsAsync(
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
            .Where(r => openWindowIds.Contains(r.WindowId) && r.Status == RequestStatus.SubmittedUnCommitted)
            .Select(r => new ReplayRequestRow
            {
                ChangeRequestId = r.Id,
                WindowId = r.WindowId,
                ReferenceNumber = r.ReferenceNumber,
                OrganisationUrn = r.OrganisationUrn,
                SubmittedById = r.SubmittedById,
                SubmittedByName = r.SubmittedByName
            })
            .ToListAsync(cancellationToken);
    }

    public Task SetStatusAsync(Guid changeRequestId, RequestStatus status, CancellationToken cancellationToken) =>
        db.ChangeRequests
            .Where(r => r.Id == changeRequestId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, status), cancellationToken);

    public async Task<int> MarkDraftsNotSubmittedForOpenWindowsAsync(DateTime now, CancellationToken cancellationToken)
    {
        var openWindowIds = await db.CheckingWindows
            .AsNoTracking()
            .Where(w => w.StartDate <= now && w.EndDate >= now)
            .Select(w => w.Id)
            .ToListAsync(cancellationToken);

        if (openWindowIds.Count == 0)
            return 0;

        return await db.ChangeRequests
            .Where(r => openWindowIds.Contains(r.WindowId)
                && (r.Status == RequestStatus.InProgress || r.Status == RequestStatus.ReadyToSubmit))
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, RequestStatus.NotSubmitted), cancellationToken);
    }
}
