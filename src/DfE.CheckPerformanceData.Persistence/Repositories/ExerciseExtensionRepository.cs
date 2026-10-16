using System.Text.Json;
using DfE.CheckPerformance.Persistence.Entities;
using DfE.CheckPerformanceData.Application.Audit;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DfE.CheckPerformanceData.Persistence.Repositories;

/// <inheritdoc cref="IExerciseExtensionRepository"/>
public sealed class ExerciseExtensionRepository(PortalDbContext dbContext) : IExerciseExtensionRepository
{
    // The audit payload is read back by WindowAdminAuditPayload with the same (camelCase) options.
    private static readonly JsonSerializerOptions AuditJson = new(JsonSerializerDefaults.Web);

    // The one index the service's own conflict check stands in front of. Any other unique violation
    // is a bug and stays an exception.
    private const string SchoolIndex = "IX_CheckingExerciseExtensionSchools_CheckingExerciseId_Laestab";

    private static bool IsSchoolTaken(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: SchoolIndex };

    public async Task<Guid> AddAsync(NewExtension extension, CancellationToken ct)
    {
        try
        {
            return await dbContext.ExecuteInTransactionAsync(async () =>
            {
                // The execution strategy may run this delegate again after a transient failure; rows
                // still tracked from the failed attempt would then be saved twice.
                dbContext.ChangeTracker.Clear();

                var entity = new CheckingExerciseExtension
                {
                    Id = Guid.NewGuid(),
                    CheckingExerciseId = extension.ExerciseId,
                    EndDate = extension.EndDate,
                    // Npgsql refuses a Utc-kind value for a timestamp without time zone.
                    CreatedAt = DateTime.SpecifyKind(extension.CreatedAtUtc, DateTimeKind.Unspecified),
                    CreatedBy = extension.Actor.DisplayName,
                    Schools = extension.Laestabs.Distinct().Select(l => new CheckingExerciseExtensionSchool
                        { CheckingExerciseId = extension.ExerciseId, Laestab = l }).ToList()
                };
                dbContext.CheckingExerciseExtensions.Add(entity);
                dbContext.AuditEntries.Add(Row(extension.WindowId, AuditActivities.ExtensionAddedAction, extension.CreatedAtUtc, extension.Actor, new
                {
                    extension.WindowId,
                    WindowTitle = await TitleAsync(extension.WindowId, ct),
                    extension.ExerciseId,
                    ExerciseType = extension.Exercise.ToString(),
                    ExtensionId = entity.Id,
                    extension.EndDate,
                    SchoolCount = entity.Schools.Count,
                    Schools = entity.Schools.Select(s => s.Laestab).ToList(),
                    ActorName = extension.Actor.DisplayName
                }));
                await dbContext.SaveChangesAsync(ct);
                await WindowEndDate.RederiveAsync(dbContext, extension.WindowId, ct);
                return entity.Id;
            }, ct);
        }
        catch (DbUpdateException ex) when (IsSchoolTaken(ex))
        {
            // The transaction has rolled back, so the extension, its schools and its audit row are
            // all gone; the service reads again and tells the admin which school and which extension.
            throw new ExtensionSchoolTakenException(ex);
        }
    }

    public async Task<bool> UpdateAsync(ExtensionEdit edit, CancellationToken ct)
    {
        try
        {
            return await dbContext.ExecuteInTransactionAsync(async () =>
            {
                dbContext.ChangeTracker.Clear();

                // Compare-and-set on the version the admin was shown, and bump it:
                // an edit that changes only the school list is guarded exactly as a date change is. The
                // window clause keeps the audit row on the window the extension really belongs to.
                int moved = await dbContext.CheckingExerciseExtensions
                    .Where(x => x.Id == edit.ExtensionId
                                && x.CheckingExerciseId == edit.ExerciseId
                                && x.Version == edit.ExpectedVersion
                                && dbContext.CheckingExercises.Any(e => e.Id == x.CheckingExerciseId && e.CheckingWindowId == edit.WindowId))
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(x => x.EndDate, edit.NewEndDate)
                        .SetProperty(x => x.Version, x => x.Version + 1), ct);
                if (moved == 0)
                {
                    return false;
                }

                // Only the difference is written, so a school that stays keeps its row and id.
                var existing = await dbContext.CheckingExerciseExtensionSchools
                    .Where(s => s.CheckingExerciseExtensionId == edit.ExtensionId)
                    .ToListAsync(ct);
                var wanted = edit.Laestabs.Distinct().ToList();
                var removed = existing.Where(s => !wanted.Contains(s.Laestab)).ToList();
                var added = wanted.Where(l => existing.All(s => s.Laestab != l)).ToList();
                dbContext.CheckingExerciseExtensionSchools.RemoveRange(removed);
                dbContext.CheckingExerciseExtensionSchools.AddRange(added.Select(l => new CheckingExerciseExtensionSchool
                    { CheckingExerciseExtensionId = edit.ExtensionId, CheckingExerciseId = edit.ExerciseId, Laestab = l }));

                dbContext.AuditEntries.Add(Row(edit.WindowId, AuditActivities.ExtensionEditedAction, edit.EditedAtUtc, edit.Actor, new
                {
                    edit.WindowId,
                    WindowTitle = await TitleAsync(edit.WindowId, ct),
                    edit.ExerciseId,
                    ExerciseType = edit.Exercise.ToString(),
                    edit.ExtensionId,
                    EndDate = edit.NewEndDate,
                    edit.PreviousEndDate,
                    edit.NewEndDate,
                    SchoolCount = wanted.Count,
                    SchoolsAdded = added,
                    SchoolsRemoved = removed.Select(s => s.Laestab).ToList(),
                    ActorName = edit.Actor.DisplayName
                }));
                await dbContext.SaveChangesAsync(ct);
                await WindowEndDate.RederiveAsync(dbContext, edit.WindowId, ct);
                return true;
            }, ct);
        }
        catch (DbUpdateException ex) when (IsSchoolTaken(ex))
        {
            // Rolled back with the version bump, so the admin's next attempt posts a version that still
            // matches. The school rows and the audit row are gone too; the service reads again and
            // tells the admin which school and which extension.
            throw new ExtensionSchoolTakenException(ex);
        }
    }

    public Task<bool> CloseEarlyAsync(ExtensionEarlyClosure closure, CancellationToken ct) =>
        dbContext.ExecuteInTransactionAsync(async () =>
        {
            dbContext.ChangeTracker.Clear();

            // As CloseExerciseEarlyAsync: a second press, a second admin's close or an edit that
            // moved the end in between leaves this false (the guard is the end date; a school-only
            // edit leaves it true, harmlessly, as closing does not depend on the school list) and
            // nothing below runs, so the audit row — the only early-closure stamp, as with the
            // exercise — can never describe a close that did not happen.
            int moved = await dbContext.CheckingExerciseExtensions
                .Where(x => x.Id == closure.ExtensionId
                            && x.CheckingExerciseId == closure.ExerciseId
                            && x.EndDate == closure.ScheduledEnd
                            && dbContext.CheckingExercises.Any(e => e.Id == x.CheckingExerciseId && e.CheckingWindowId == closure.WindowId))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.EndDate, closure.NewEndDate)
                    // Bumped so an edit page drawn before the close is refused as stale.
                    .SetProperty(x => x.Version, x => x.Version + 1), ct);
            if (moved == 0)
            {
                return false;
            }

            dbContext.AuditEntries.Add(Row(closure.WindowId, AuditActivities.ExtensionClosedEarlyAction, closure.ClosedAtUtc, closure.Actor, new
            {
                closure.WindowId,
                WindowTitle = await TitleAsync(closure.WindowId, ct),
                closure.ExerciseId,
                ExerciseType = closure.Exercise.ToString(),
                closure.ExtensionId,
                EndDate = closure.NewEndDate,
                closure.ScheduledEnd,
                closure.NewEndDate,
                ClosedEarly = true,
                // closedBy as the exercise's ClosedEarly row has it; actorName as every extension row has it.
                ClosedBy = closure.Actor.DisplayName,
                ActorName = closure.Actor.DisplayName,
                closure.ClosedAtUtc
            }));
            await dbContext.SaveChangesAsync(ct);
            await WindowEndDate.RederiveAsync(dbContext, closure.WindowId, ct);
            return true;
        }, ct);

    public Task RecordRequestsSentAsync(ExtensionRequestsSent record, CancellationToken ct) =>
        dbContext.ExecuteInTransactionAsync(async () =>
        {
            dbContext.ChangeTracker.Clear();
            dbContext.AuditEntries.Add(Row(record.WindowId, AuditActivities.ExtensionRequestsSentAction, record.SentAtUtc, record.Actor, new
            {
                record.WindowId,
                WindowTitle = await TitleAsync(record.WindowId, ct),
                record.ExerciseId,
                ExerciseType = record.Exercise.ToString(),
                record.ExtensionId,
                EndDate = record.ExtensionEnd,
                record.ExtensionEnd,
                record.RequestsSent,
                record.DraftsCancelled,
                ActorName = record.Actor.DisplayName
            }));
            await dbContext.SaveChangesAsync(ct);
        }, ct);

    public async Task<bool> MarkRequestsSentAsync(Guid extensionId, DateTime atUtc, CancellationToken ct)
    {
        var at = DateTime.SpecifyKind(atUtc, DateTimeKind.Unspecified);
        // Only the first sweep stamps it, whichever of the manual send and the hand-over runs first.
        return await dbContext.CheckingExerciseExtensions
            .Where(x => x.Id == extensionId && x.RequestsSentAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RequestsSentAt, at), ct) == 1;
    }

    private Task<string> TitleAsync(Guid windowId, CancellationToken ct) =>
        dbContext.CheckingWindows.Where(w => w.Id == windowId).Select(w => w.Title).SingleAsync(ct);

    // Written by hand because ExecuteUpdate bypasses the change tracker, and because the generic
    // capture could say neither what the admin did nor who by name.
    private static AuditEntry Row(Guid windowId, string action, DateTime atUtc, ExtensionActor actor, object payload) => new()
    {
        EntityType = AuditActivities.WindowAdmin,
        EntityId = windowId.ToString(),
        Action = action,
        Timestamp = atUtc,
        UserId = actor.UserId,
        NewValues = JsonSerializer.Serialize(payload, AuditJson)
    };
}
