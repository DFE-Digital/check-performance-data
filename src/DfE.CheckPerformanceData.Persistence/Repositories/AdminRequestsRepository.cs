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
                ProcessingStatus = r.ProcessingStatus,
                DecisionTrace = r.DecisionTrace
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ReplayRequestRow>> GetDecidedRequestsForExerciseAsync(
        Guid windowId, Guid exerciseId, CancellationToken cancellationToken)
    {
        return await SubmittedAmendmentsForExercise(windowId, exerciseId)
            .AsNoTracking()
            .Where(r => r.ProcessingStatus == ProcessingStatus.Decided && r.CrmId == null)
            // First come, first sent. Id breaks a tie so the order is stable.
            .OrderBy(r => r.Submitted)
            .ThenBy(r => r.Id)
            .Select(r => new ReplayRequestRow
            {
                ChangeRequestId = r.Id,
                WindowId = r.WindowId,
                ReferenceNumber = r.ReferenceNumber
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<int> CountWaitingRequestsForExerciseAsync(
        Guid windowId, Guid exerciseId, CancellationToken cancellationToken)
    {
        return await SubmittedAmendmentsForExercise(windowId, exerciseId)
            .CountAsync(r => r.ProcessingStatus == null, cancellationToken);
    }

    public async Task<bool> MarkTicketQueuedAsync(Guid changeRequestId, CancellationToken cancellationToken) =>
        await db.ChangeRequests
            .Where(r => r.Id == changeRequestId && r.ProcessingStatus == ProcessingStatus.Decided)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.ProcessingStatus, ProcessingStatus.TicketQueued),
                cancellationToken) == 1;

    public Task ExecuteInTransactionAsync(Func<Task> work, CancellationToken cancellationToken) =>
        db.ExecuteInTransactionAsync(async () =>
        {
            // A queue message added in a failed attempt stays tracked as Added. Forget it, or the
            // retry strategy's next attempt would insert it a second time.
            db.ChangeTracker.Clear();
            try
            {
                await work();
            }
            catch
            {
                db.ChangeTracker.Clear();
                throw;
            }
        }, cancellationToken);

    // One predicate for what the sweep sends and what it reports as waiting, so the two can never
    // describe different populations.
    private IQueryable<Entities.ChangeRequest> SubmittedAmendmentsForExercise(Guid windowId, Guid exerciseId) =>
        db.ChangeRequests
            .Where(r => r.WindowId == windowId
                && r.CheckingExerciseId != null
                && r.CheckingExerciseId == exerciseId
                && r.RequestType == RequestType.Amendment
                && r.Status == RequestStatus.Submitted);

    public async Task<int> MarkDraftsNotSubmittedForExerciseAsync(
        Guid windowId, Guid exerciseId, CancellationToken cancellationToken)
    {
        return await DraftsForExercise(windowId, exerciseId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, RequestStatus.NotSubmitted), cancellationToken);
    }

    public async Task<int> CountDraftsForExerciseAsync(
        Guid windowId, Guid exerciseId, CancellationToken cancellationToken)
    {
        return await DraftsForExercise(windowId, exerciseId).CountAsync(cancellationToken);
    }

    // One predicate for the count and the update, so the confirmation page cannot promise a
    // different number of drafts from the one the close actually cancels.
    private IQueryable<Entities.ChangeRequest> DraftsForExercise(Guid windowId, Guid exerciseId) =>
        db.ChangeRequests
            .Where(r => r.WindowId == windowId
                && r.CheckingExerciseId != null
                && r.CheckingExerciseId == exerciseId
                && (r.Status == RequestStatus.InProgress || r.Status == RequestStatus.ReadyToSubmit));
}
