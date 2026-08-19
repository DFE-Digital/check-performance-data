using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using WindowValidated = DfE.CheckPerformanceData.Persistence.Entities.WindowValidated;

namespace DfE.CheckPerformanceData.Persistence.Repositories;

public sealed class WindowRepository(PortalDbContext dbContext) : IWindowRepository
{
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
                Id = w.Id,
                IngressFile = w.IngressFile,
                IngressFileChecksum = w.IngressFileChecksum,
                SchemaFile = w.SchemaFile,
                SchemaFileChecksum = w.SchemaFileChecksum,
                Validated = w.Validated != null,
                ValidatedAt = (w.Validated != null ? w.Validated.ValidatedAt : null),
                Exercises = w.CheckingExercises
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
                Id = w.Id,
                IngressFile = w.IngressFile,
                IngressFileChecksum = w.IngressFileChecksum,
                SchemaFile = w.SchemaFile,
                SchemaFileChecksum = w.SchemaFileChecksum,
                Validated = w.Validated != null,
                ValidatedAt = (w.Validated != null ? w.Validated.ValidatedAt : null),
                Exercises = w.CheckingExercises
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
            window.IngressFile,
            window.IngressFileChecksum,
            window.SchemaFile,
            window.SchemaFileChecksum
        });

        entity.Validated = new WindowValidated { ValidatedAt = window.ValidatedAt ?? DateTime.UtcNow };

        SyncExercises(entity, window.Exercises);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    // Exercises are keyed by type within a window (the unique index), datasets by name within an
    // exercise: existing rows are updated in place so their Ids — and any files already uploaded
    // against them — survive, new ones are added, and rows no longer wanted are removed.
    private static void SyncExercises(CheckingWindow entity, List<CheckingExerciseDto> wanted)
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

            SyncDatasets(entity, existing, dto.Datasets);
        }

        foreach (CheckingExercise stale in entity.CheckingExercises
                     .Where(e => wanted.All(x => x.ExerciseType != e.ExerciseType))
                     .ToList())
        {
            entity.CheckingExercises.Remove(stale);
        }
    }

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
            SortOrder = dto.SortOrder
        };

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
            IngressFile = window.IngressFile,
            IngressFileChecksum = window.IngressFileChecksum,
            SchemaFile = window.SchemaFile,
            SchemaFileChecksum = window.SchemaFileChecksum,
            Validated = new WindowValidated() { ValidatedAt = window.ValidatedAt ?? DateTime.UtcNow }
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
                Datasets = dto.Datasets.Select(d => NewDataset(entity, d)).ToList()
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
            IngressFile = entity.IngressFile,
            IngressFileChecksum = entity.IngressFileChecksum,
            SchemaFile = entity.SchemaFile,
            SchemaFileChecksum = entity.SchemaFileChecksum,
            Validated = entity.Validated != null,
            ValidatedAt = entity.Validated?.ValidatedAt
        };
    }
}