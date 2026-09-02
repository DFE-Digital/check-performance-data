using DfE.CheckPerformanceData.Application.AdminRequests;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Persistence.Repositories;

public sealed class AdminRequestsRepository(IPortalDbContext db) : IAdminRequestsRepository
{
    public async Task<IReadOnlyList<AdminRequestRow>> GetForWindowAsync(
        Guid windowId, CheckingExerciseType? exercise, CancellationToken cancellationToken)
    {
        var query = db.ChangeRequests
            .AsNoTracking()
            .Where(r => r.WindowId == windowId);

        if (exercise is { } type)
        {
            // Resolve the type to this window's own exercise row rather than comparing types across
            // the join: CheckingExerciseId is a row id, and two windows running the same exercise
            // are still two different exercises. A window with no row of that type yields no id and
            // therefore no rows, which is the correct empty answer rather than an unfiltered list.
            var exerciseId = await db.CheckingExercises
                .AsNoTracking()
                .Where(e => e.CheckingWindowId == windowId && e.ExerciseType == type)
                .Select(e => (Guid?)e.Id)
                .FirstOrDefaultAsync(cancellationToken);

            query = query.Where(r => r.CheckingExerciseId != null && r.CheckingExerciseId == exerciseId);
        }

        return await query
            .OrderByDescending(r => r.Submitted)
            .Select(r => new AdminRequestRow
            {
                ReferenceNumber = r.ReferenceNumber,
                OrganisationUrn = r.OrganisationUrn,
                PupilFirstname = r.PupilFirstname,
                PupilSurname = r.PupilSurname,
                RequestTypeDescription = r.RequestTypeDescription,
                Exercise = db.CheckingExercises
                    .Where(e => e.Id == r.CheckingExerciseId)
                    .Select(e => (CheckingExerciseType?)e.ExerciseType)
                    .FirstOrDefault(),
                Status = r.Status,
                SubmittedByName = r.SubmittedByName,
                Submitted = r.Submitted,
                Outcome = r.Outcome,
                MatchedRule = r.MatchedRuleId,
                DecidedAtUtc = r.DecidedAtUtc,
                CrmId = r.CrmId,
                DecisionTrace = r.DecisionTrace
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
