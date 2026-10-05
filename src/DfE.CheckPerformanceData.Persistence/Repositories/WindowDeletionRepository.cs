using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Persistence.Repositories;

/// <inheritdoc cref="IWindowDeletionRepository"/>
public sealed class WindowDeletionRepository(PortalDbContext dbContext) : IWindowDeletionRepository
{
    public async Task<IReadOnlyDictionary<RequestStatus, int>> CountRequestsByStatusAsync(
        Guid windowId, CancellationToken cancellationToken) =>
        await dbContext.ChangeRequests
            .Where(r => r.WindowId == windowId)
            .GroupBy(r => r.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.Count, cancellationToken);

    public Task<int> CountSentForProcessingAsync(Guid windowId, CancellationToken cancellationToken) =>
        dbContext.ChangeRequests.CountAsync(r => r.WindowId == windowId
            && r.Status == RequestStatus.Submitted
            && (r.ProcessingStatus == ProcessingStatus.TicketQueued
                || r.ProcessingStatus == ProcessingStatus.TicketCreating
                || r.ProcessingStatus == ProcessingStatus.TicketCreated), cancellationToken);

    public Task<int> CountEgressRunsAsync(Guid windowId, CancellationToken cancellationToken) =>
        dbContext.EgressRuns.CountAsync(r => r.WindowId == windowId, cancellationToken);

    public Task<bool> DeleteAsync(Guid windowId, CancellationToken cancellationToken) =>
        dbContext.ExecuteInTransactionAsync(async () =>
        {
            var window = await dbContext.CheckingWindows
                .SingleOrDefaultAsync(w => w.Id == windowId, cancellationToken);
            if (window is null)
                return false;

            // Both keys to the window are ON DELETE RESTRICT, so these rows go by hand, before the
            // window. Bulk deletes: a window can hold thousands of requests, and each one is not
            // an audit record of its own — the window's delete below is.
            await dbContext.ChangeRequests
                .Where(r => r.WindowId == windowId)
                .ExecuteDeleteAsync(cancellationToken);

            // An egress run's outputs and learner rows cascade from the run.
            await dbContext.EgressRuns
                .Where(r => r.WindowId == windowId)
                .ExecuteDeleteAsync(cancellationToken);

            // An exercise's pointer to the exercise it replaces is also RESTRICT, and RESTRICT is
            // checked row by row inside the cascade, so a window holding a replaced exercise and
            // its replacement cannot cascade until the pointer is cleared. Matched on the target,
            // not on the window, so a pointer from any other window is cleared too.
            var exerciseIds = dbContext.CheckingExercises
                .Where(e => e.CheckingWindowId == windowId)
                .Select(e => e.Id);
            await dbContext.CheckingExercises
                .Where(e => e.ReplacesCheckingExerciseId != null
                            && exerciseIds.Contains(e.ReplacesCheckingExerciseId.Value))
                .ExecuteUpdateAsync(
                    s => s.SetProperty(e => e.ReplacesCheckingExerciseId, (Guid?)null),
                    cancellationToken);

            // Tracked, so SaveChangesAsync writes the audit entry. Exercises, datasets, releases
            // and release files cascade in the database.
            dbContext.CheckingWindows.Remove(window);
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }, cancellationToken);
}
