using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Persistence.Repositories;

/// <inheritdoc cref="IExerciseDeletionRepository"/>
public sealed class ExerciseDeletionRepository(PortalDbContext dbContext) : IExerciseDeletionRepository
{
    public async Task<IReadOnlyDictionary<RequestStatus, int>> CountRequestsByStatusAsync(
        Guid exerciseId, CancellationToken cancellationToken) =>
        await dbContext.ChangeRequests
            .Where(r => r.CheckingExerciseId == exerciseId)
            .GroupBy(r => r.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.Count, cancellationToken);

    public Task<int> CountSentForProcessingAsync(Guid exerciseId, CancellationToken cancellationToken) =>
        dbContext.ChangeRequests.CountAsync(r => r.CheckingExerciseId == exerciseId
            && r.Status == RequestStatus.Submitted
            && (r.ProcessingStatus == ProcessingStatus.TicketQueued
                || r.ProcessingStatus == ProcessingStatus.TicketCreating
                || r.ProcessingStatus == ProcessingStatus.TicketCreated), cancellationToken);

    public Task<DeletedExercise?> DeleteAsync(Guid windowId, Guid exerciseId, CancellationToken cancellationToken) =>
        dbContext.ExecuteInTransactionAsync(async () =>
        {
            if (!await dbContext.CheckingExercises.AnyAsync(
                    e => e.Id == exerciseId && e.CheckingWindowId == windowId, cancellationToken))
                return null;

            // The key from a change request is SET NULL, which would leave rows that no Close can
            // reach and no admin can filter by. The admin has agreed to lose them, so they go.
            await dbContext.ChangeRequests
                .Where(r => r.CheckingExerciseId == exerciseId)
                .ExecuteDeleteAsync(cancellationToken);

            // An exercise that replaces this one points at it with a RESTRICT key. The replacement
            // stays; it simply no longer replaces anything.
            await dbContext.CheckingExercises
                .Where(e => e.ReplacesCheckingExerciseId == exerciseId)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(e => e.ReplacesCheckingExerciseId, (Guid?)null),
                    cancellationToken);

            // Loaded after the bulk update, so no tracked row still holds the pointer: EF refuses
            // to delete a principal while a tracked dependent points at it through a RESTRICT key.
            var window = await dbContext.CheckingWindows
                .Include(w => w.CheckingExercises)
                .SingleAsync(w => w.Id == windowId, cancellationToken);
            var exercise = window.CheckingExercises.Single(e => e.Id == exerciseId);

            // Tracked, so SaveChangesAsync writes the audit entry. Datasets, releases and release
            // files cascade in the database.
            dbContext.CheckingExercises.Remove(exercise);

            // A window's dates are the union of its exercises' dates
            // (CheckingWindowDto.DeriveDatesFromExercises). With no exercise left there is nothing
            // to derive from, so the dates stay as they were.
            var remaining = window.CheckingExercises.Where(e => e.Id != exerciseId).ToList();
            if (remaining.Count > 0)
            {
                var entry = dbContext.Entry(window);
                entry.Property(w => w.StartDate).CurrentValue = remaining.Min(e => e.StartDate);
                entry.Property(w => w.EndDate).CurrentValue = remaining.Max(e => e.EndDate);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            return new DeletedExercise(exercise.UsesExerciseStorage);
        }, cancellationToken);
}
