using System.Text.Json;
using DfE.CheckPerformance.Persistence.Entities;
using DfE.CheckPerformanceData.Application.Audit;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Persistence.Repositories;

public sealed class WindowRepository(PortalDbContext dbContext) : IWindowRepository
{
    // The audit payload is read back by WindowAdminAuditPayload with the same (camelCase) options.
    private static readonly JsonSerializerOptions AuditJson = new(JsonSerializerDefaults.Web);

    public async Task<List<CheckingWindowDto>> GetAllWindowsAsync(CancellationToken cancellationToken) =>
        await dbContext.CheckingWindows
            .AsNoTracking()
            .Select(w => new CheckingWindowDto
            {
                StartDate = w.StartDate,
                EndDate = w.EndDate,
                KeyStage = w.KeyStage,
                CheckingWindowType = w.CheckingWindowType,
                Title = w.Title,
                TurnaroundCommitment = w.TurnaroundCommitment,
                NextOpportunity = w.NextOpportunity,
                Id = w.Id,
                IngressFile = w.IngressFile,
                IngressFileChecksum = w.IngressFileChecksum,
                SchemaFile = w.SchemaFile,
                SchemaFileChecksum = w.SchemaFileChecksum,
                Exercises = w.CheckingExercises
                    .OrderBy(e => e.SortOrder)
                    .Select(e => new CheckingExerciseDto
                    {
                        Id = e.Id,
                        ExerciseType = e.ExerciseType,
                        StartDate = e.StartDate,
                        EndDate = e.EndDate,
                        SortOrder = e.SortOrder,
                        // #319: the validation stamp lives on the exercise now. The checksums say
                        // which files it was taken over, so a stamp left behind by a since-replaced
                        // ingress file reads as stale rather than as validated.
                        ValidatedAt = e.Validated != null ? e.Validated.ValidatedAt : null,
                        ValidatedIngressChecksum =
                            e.Validated != null ? e.Validated.IngressValidationChecksum : string.Empty,
                        ValidatedSchemaChecksum =
                            e.Validated != null ? e.Validated.SchemaValidationChecksum : string.Empty,
                        Datasets = e.Datasets
                            .OrderBy(d => d.SortOrder)
                            .Select(d => new CheckingWindowDatasetDto
                            {
                                Id = d.Id,
                                Name = d.Name,
                                IngressFile = d.IngressFile,
                                IngressFileChecksum = d.IngressFileChecksum,
                                SchemaFile = d.SchemaFile,
                                SchemaFileChecksum = d.SchemaFileChecksum,
                                Included = d.Included,
                                SourceFile = d.SourceFile,
                                Required = d.Required,
                                SortOrder = d.SortOrder
                            })
                            .ToList()
                    })
                    .ToList()
            })
            .ToListAsync(cancellationToken);
    
    public async Task<CheckingWindowDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        await dbContext.CheckingWindows
            .AsNoTracking()
            .Where(w => w.Id == id)
            .Select(w => new CheckingWindowDto
            {
                StartDate = w.StartDate,
                EndDate = w.EndDate,
                KeyStage = w.KeyStage,
                CheckingWindowType = w.CheckingWindowType,
                Title = w.Title,
                TurnaroundCommitment = w.TurnaroundCommitment,
                NextOpportunity = w.NextOpportunity,
                Id = w.Id,
                IngressFile = w.IngressFile,
                IngressFileChecksum = w.IngressFileChecksum,
                SchemaFile = w.SchemaFile,
                SchemaFileChecksum = w.SchemaFileChecksum,
                Exercises = w.CheckingExercises
                    .OrderBy(e => e.SortOrder)
                    .Select(e => new CheckingExerciseDto
                    {
                        Id = e.Id,
                        ExerciseType = e.ExerciseType,
                        StartDate = e.StartDate,
                        EndDate = e.EndDate,
                        SortOrder = e.SortOrder,
                        // #319: the validation stamp lives on the exercise now. The checksums say
                        // which files it was taken over, so a stamp left behind by a since-replaced
                        // ingress file reads as stale rather than as validated.
                        ValidatedAt = e.Validated != null ? e.Validated.ValidatedAt : null,
                        ValidatedIngressChecksum =
                            e.Validated != null ? e.Validated.IngressValidationChecksum : string.Empty,
                        ValidatedSchemaChecksum =
                            e.Validated != null ? e.Validated.SchemaValidationChecksum : string.Empty,
                        Datasets = e.Datasets
                            .OrderBy(d => d.SortOrder)
                            .Select(d => new CheckingWindowDatasetDto
                            {
                                Id = d.Id,
                                Name = d.Name,
                                IngressFile = d.IngressFile,
                                IngressFileChecksum = d.IngressFileChecksum,
                                SchemaFile = d.SchemaFile,
                                SchemaFileChecksum = d.SchemaFileChecksum,
                                Included = d.Included,
                                SourceFile = d.SourceFile,
                                Required = d.Required,
                                SortOrder = d.SortOrder
                            })
                            .ToList()
                    })
                    .ToList()
            })
            .FirstOrDefaultAsync(cancellationToken);

    public async Task UpdateAsync(CheckingWindowDto window, CancellationToken cancellationToken)
    {
        // Loaded and mutated rather than Update(new CheckingWindow{...}) — a detached overwrite
        // would leave the window's exercise and dataset rows untracked and strand them.
        CheckingWindow entity = await dbContext.CheckingWindows
            .Include(w => w.CheckingExercises)
            .ThenInclude(e => e.Datasets)
            .SingleAsync(w => w.Id == window.Id, cancellationToken);

        dbContext.Entry(entity).CurrentValues.SetValues(new
        {
            window.StartDate,
            window.EndDate,
            window.KeyStage,
            window.CheckingWindowType,
            window.Title,
            window.TurnaroundCommitment,
            window.NextOpportunity,
            window.IngressFile,
            window.IngressFileChecksum,
            window.SchemaFile,
            window.SchemaFileChecksum
        });

        SyncExercises(entity, window.Exercises);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    // Exercises are keyed by type within a window (the unique index), datasets by name within an
    // exercise: existing rows are updated in place so their Ids — and any files already uploaded
    // against them — survive, new ones are added, and rows no longer wanted are removed.
    private void SyncExercises(CheckingWindow entity, List<CheckingExerciseDto> wanted)
    {
        if (wanted.Count == 0)
        {
            return;
        }

        foreach (CheckingExerciseDto dto in wanted)
        {
            CheckingExercise? existing =
                entity.CheckingExercises.SingleOrDefault(e => e.ExerciseType == dto.ExerciseType);

            if (existing is null)
            {
                existing = new CheckingExercise
                {
                    CheckingWindowId = entity.Id,
                    ExerciseType = dto.ExerciseType,
                    StartDate = dto.StartDate,
                    EndDate = dto.EndDate,
                    SortOrder = dto.SortOrder
                };
                entity.CheckingExercises.Add(existing);
            }
            else
            {
                // #319: an exercise's dates are editable now, so an existing row has to take them.
                // Before the wizard captured them nothing could change an exercise's dates, and
                // this loop only ever reconciled datasets.
                dbContext.Entry(existing).CurrentValues.SetValues(new
                {
                    dto.StartDate,
                    dto.EndDate,
                    dto.SortOrder
                });
            }

            existing.Validated = StampFor(dto);

            SyncDatasets(entity, existing, dto.Datasets);
        }

        foreach (CheckingExercise stale in entity.CheckingExercises
                     .Where(e => wanted.All(x => x.ExerciseType != e.ExerciseType))
                     .ToList())
        {
            entity.CheckingExercises.Remove(stale);
        }
    }

    // Null when the exercise has never validated. Written from the DTO rather than invented here:
    // the old window-level stamp was set unconditionally on every create and update, so it said
    // nothing at all about whether anything had been validated.
    private static ExerciseValidated? StampFor(CheckingExerciseDto dto) =>
        dto.ValidatedAt is null
            ? null
            : new ExerciseValidated
            {
                ValidatedAt = dto.ValidatedAt.Value,
                IngressValidationChecksum = dto.ValidatedIngressChecksum,
                SchemaValidationChecksum = dto.ValidatedSchemaChecksum
            };

    private static void SyncDatasets(
        CheckingWindow window, CheckingExercise exercise, List<CheckingWindowDatasetDto> wanted)
    {
        if (wanted.Count == 0)
        {
            return;
        }

        foreach (CheckingWindowDatasetDto dto in wanted)
        {
            CheckingWindowDataset? existing = exercise.Datasets.SingleOrDefault(d => d.Name == dto.Name);

            if (existing is null)
            {
                exercise.Datasets.Add(NewDataset(window, dto));
                continue;
            }

            existing.IngressFile = dto.IngressFile;
            existing.IngressFileChecksum = dto.IngressFileChecksum;
            existing.SchemaFile = dto.SchemaFile;
            existing.SchemaFileChecksum = dto.SchemaFileChecksum;
        }

        foreach (CheckingWindowDataset stale in exercise.Datasets
                     .Where(d => wanted.All(x => x.Name != d.Name))
                     .ToList())
        {
            exercise.Datasets.Remove(stale);
        }
    }

    // The legacy CheckingWindowId column is still written, though nothing reads it: it is what
    // makes a rollback to the previous release safe. The follow-up ticket that drops the column
    // drops this too.
    private static CheckingWindowDataset NewDataset(CheckingWindow window, CheckingWindowDatasetDto dto) =>
        new()
        {
            CheckingWindowId = window.Id,
            Name = dto.Name,
            IngressFile = dto.IngressFile,
            IngressFileChecksum = dto.IngressFileChecksum,
            SchemaFile = dto.SchemaFile,
            SchemaFileChecksum = dto.SchemaFileChecksum,
            Included = dto.Included,
            SourceFile = dto.SourceFile,
            Required = dto.Required,
            SortOrder = dto.SortOrder
        };

    public async Task<bool> CloseExerciseEarlyAsync(
        ExerciseEarlyClosure closure, CancellationToken cancellationToken) =>
        await dbContext.ExecuteInTransactionAsync(async () =>
        {
            // The execution strategy may run this delegate again after a transient failure; an
            // AuditEntry still tracked from the failed attempt would then be saved twice.
            dbContext.ChangeTracker.Clear();

            // Compare-and-set on the end date the admin was shown. A second press, a second admin,
            // or a date edit in between all leave it false, and then nothing below runs — so an
            // exercise is closed early at most once and the audit row can never describe a close
            // that did not happen.
            int moved = await dbContext.CheckingExercises
                .Where(e => e.CheckingWindowId == closure.WindowId
                            && e.ExerciseType == closure.Exercise
                            && e.EndDate == closure.ScheduledEnd)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(e => e.EndDate, closure.NewEndDate), cancellationToken);
            if (moved == 0)
            {
                return false;
            }

            // The window's own end date is the latest exercise end (#319) and is never typed, so
            // it is re-derived here exactly as WindowService.UpdateAsync derives it on an edit.
            DateTime latestEnd = await dbContext.CheckingExercises
                .Where(e => e.CheckingWindowId == closure.WindowId)
                .MaxAsync(e => e.EndDate, cancellationToken);
            await dbContext.CheckingWindows
                .Where(w => w.Id == closure.WindowId)
                .ExecuteUpdateAsync(s => s.SetProperty(w => w.EndDate, latestEnd), cancellationToken);

            string title = await dbContext.CheckingWindows
                .Where(w => w.Id == closure.WindowId)
                .Select(w => w.Title)
                .SingleAsync(cancellationToken);

            // Written by hand because ExecuteUpdate bypasses the change tracker, and because the
            // generic capture could only say "EndDate changed" — not that this was an early close,
            // nor who by name.
            dbContext.AuditEntries.Add(new AuditEntry
            {
                EntityType = AuditActivities.WindowAdmin,
                EntityId = closure.WindowId.ToString(),
                Action = AuditActivities.ClosedEarlyAction,
                Timestamp = closure.ClosedAtUtc,
                UserId = closure.UserId,
                NewValues = JsonSerializer.Serialize(new
                {
                    closure.WindowId,
                    WindowTitle = title,
                    ExerciseType = closure.Exercise.ToString(),
                    closure.ScheduledEnd,
                    closure.NewEndDate,
                    ClosedEarly = true,
                    ClosedBy = closure.ClosedByName,
                    closure.ClosedAtUtc
                }, AuditJson)
            });
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }, cancellationToken);

    public async Task<CheckingWindowDto> CreateAsync(CheckingWindowDto window, CancellationToken cancellationToken)
    {
        var entity = new CheckingWindow
        {
            // The id is assigned here rather than by the database default, because the legacy
            // CheckingWindowId stamped onto each dataset row below needs it before the save.
            Id = window.Id == Guid.Empty ? Guid.NewGuid() : window.Id,
            StartDate = window.StartDate,
            EndDate = window.EndDate,
            KeyStage = window.KeyStage,
            CheckingWindowType = window.CheckingWindowType,
            Title = window.Title,
            TurnaroundCommitment = window.TurnaroundCommitment,
            NextOpportunity = window.NextOpportunity,
            IngressFile = window.IngressFile,
            IngressFileChecksum = window.IngressFileChecksum,
            SchemaFile = window.SchemaFile,
            SchemaFileChecksum = window.SchemaFileChecksum
        };

        // A window is born with its exercises, each holding the dataset slots its type requires.
        // WindowService supplies a pupil-data exercise when the caller names none.
        foreach (CheckingExerciseDto dto in window.Exercises.OrderBy(e => e.SortOrder))
        {
            entity.CheckingExercises.Add(new CheckingExercise
            {
                ExerciseType = dto.ExerciseType,
                StartDate = dto.StartDate,
                EndDate = dto.EndDate,
                SortOrder = dto.SortOrder,
                Datasets = dto.Datasets.Select(d => NewDataset(entity, d)).ToList(),
                Validated = StampFor(dto)
            });
        }

        await dbContext.CheckingWindows.AddAsync(entity, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new CheckingWindowDto
        {
            Id = entity.Id,
            StartDate = entity.StartDate,
            EndDate = entity.EndDate,
            KeyStage = entity.KeyStage,
            CheckingWindowType = entity.CheckingWindowType,
            Title = entity.Title,
            TurnaroundCommitment = entity.TurnaroundCommitment,
            NextOpportunity = entity.NextOpportunity,
            IngressFile = entity.IngressFile,
            IngressFileChecksum = entity.IngressFileChecksum,
            SchemaFile = entity.SchemaFile,
            SchemaFileChecksum = entity.SchemaFileChecksum,
            Exercises = entity.CheckingExercises
                .OrderBy(e => e.SortOrder)
                .Select(e => new CheckingExerciseDto
                {
                    Id = e.Id,
                    ExerciseType = e.ExerciseType,
                    StartDate = e.StartDate,
                    EndDate = e.EndDate,
                    SortOrder = e.SortOrder,
                    Datasets = e.Datasets
                        .OrderBy(d => d.SortOrder)
                        .Select(d => new CheckingWindowDatasetDto
                        {
                            Id = d.Id,
                            Name = d.Name,
                            IngressFile = d.IngressFile,
                            IngressFileChecksum = d.IngressFileChecksum,
                            SchemaFile = d.SchemaFile,
                            SchemaFileChecksum = d.SchemaFileChecksum,
                            Included = d.Included,
                            SourceFile = d.SourceFile,
                            Required = d.Required,
                            SortOrder = d.SortOrder
                        })
                        .ToList()
                })
                .ToList()
        };
    }
}