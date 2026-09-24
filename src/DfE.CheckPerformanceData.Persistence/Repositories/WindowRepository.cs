using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Persistence.Repositories;

public sealed class WindowRepository(PortalDbContext dbContext) : IWindowRepository
{
    public async Task<List<CheckingWindowDto>> GetAllWindowsAsync(CancellationToken cancellationToken) =>
        await dbContext.CheckingWindows
            .AsNoTracking()
            // Datasets and Releases are sibling collections. In one query each release file would
            // repeat every dataset row; split queries load each collection once.
            .AsSplitQuery()
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
                        UsesExerciseStorage = e.UsesExerciseStorage,
                        Name = e.Name,
                        TabName = e.TabName,
                        TabOrder = e.TabOrder,
                        IsEnabled = e.IsEnabled,
                        DisplayOnly = e.DisplayOnly,
                        VisibleFrom = e.VisibleFrom,
                        VisibleUntil = e.VisibleUntil,
                        WindowStart = w.StartDate,
                        WindowEnd = w.EndDate,
                        ReplacesCheckingExerciseId = e.ReplacesCheckingExerciseId,
                        CurrentReleaseId = e.CurrentReleaseId,
                        Layout = e.Layout,
                        // Oldest first. The school-facing display reads its schemas from the current release, and
                        // the admin exercise page lists every release so an earlier one can be made live again.
                        Releases = e.Releases
                            .OrderBy(r => r.Number)
                            .Select(r => new CheckingExerciseReleaseDto
                            {
                                Id = r.Id,
                                Number = r.Number,
                                PublishedAt = r.PublishedAt,
                                PublishedBy = r.PublishedBy,
                                FilesWritten = r.FilesWritten,
                                Files = r.Files
                                    .OrderBy(f => f.SortOrder)
                                    .Select(f => new CheckingExerciseReleaseFileDto
                                    {
                                        DatasetId = f.DatasetId,
                                        DatasetName = f.DatasetName,
                                        FeedsJourney = f.FeedsJourney,
                                        Included = f.Included,
                                        SourceFile = f.SourceFile,
                                        IngressFile = f.IngressFile,
                                        IngressFileChecksum = f.IngressFileChecksum,
                                        SchemaFile = f.SchemaFile,
                                        SchemaFileChecksum = f.SchemaFileChecksum,
                                        SortOrder = f.SortOrder
                                    })
                                    .ToList()
                            })
                            .ToList(),
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
                                FeedsJourney = d.FeedsJourney,
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
            // Datasets and Releases are sibling collections. In one query each release file would
            // repeat every dataset row; split queries load each collection once.
            .AsSplitQuery()
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
                        UsesExerciseStorage = e.UsesExerciseStorage,
                        Name = e.Name,
                        TabName = e.TabName,
                        TabOrder = e.TabOrder,
                        IsEnabled = e.IsEnabled,
                        DisplayOnly = e.DisplayOnly,
                        VisibleFrom = e.VisibleFrom,
                        VisibleUntil = e.VisibleUntil,
                        WindowStart = w.StartDate,
                        WindowEnd = w.EndDate,
                        ReplacesCheckingExerciseId = e.ReplacesCheckingExerciseId,
                        CurrentReleaseId = e.CurrentReleaseId,
                        Layout = e.Layout,
                        // Oldest first. The school-facing display reads its schemas from the current release, and
                        // the admin exercise page lists every release so an earlier one can be made live again.
                        Releases = e.Releases
                            .OrderBy(r => r.Number)
                            .Select(r => new CheckingExerciseReleaseDto
                            {
                                Id = r.Id,
                                Number = r.Number,
                                PublishedAt = r.PublishedAt,
                                PublishedBy = r.PublishedBy,
                                FilesWritten = r.FilesWritten,
                                Files = r.Files
                                    .OrderBy(f => f.SortOrder)
                                    .Select(f => new CheckingExerciseReleaseFileDto
                                    {
                                        DatasetId = f.DatasetId,
                                        DatasetName = f.DatasetName,
                                        FeedsJourney = f.FeedsJourney,
                                        Included = f.Included,
                                        SourceFile = f.SourceFile,
                                        IngressFile = f.IngressFile,
                                        IngressFileChecksum = f.IngressFileChecksum,
                                        SchemaFile = f.SchemaFile,
                                        SchemaFileChecksum = f.SchemaFileChecksum,
                                        SortOrder = f.SortOrder
                                    })
                                    .ToList()
                            })
                            .ToList(),
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
                                FeedsJourney = d.FeedsJourney,
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

    // Keyed by id since #466. Type is no longer unique within a window, and a typeless share has
    // no type to key on at all. An empty id is a row the wizard has just built. Datasets stay
    // keyed by name within an exercise: existing rows are updated in place so their Ids — and any
    // files already uploaded against them — survive, new ones are added, and rows no longer
    // wanted are removed (or, for a configured exercise, disabled — see the sweep below).
    private void SyncExercises(CheckingWindow entity, List<CheckingExerciseDto> wanted)
    {
        if (wanted.Count == 0)
        {
            return;
        }

        foreach (CheckingExerciseDto dto in wanted)
        {
            CheckingExercise? existing =
                dto.Id != Guid.Empty
                    ? entity.CheckingExercises.SingleOrDefault(e => e.Id == dto.Id)
                    : entity.CheckingExercises.SingleOrDefault(e => e.ExerciseType == dto.ExerciseType);

            if (existing is null)
            {
                existing = new CheckingExercise
                {
                    Id = dto.Id == Guid.Empty ? Guid.NewGuid() : dto.Id,
                    CheckingWindowId = entity.Id,
                    UsesExerciseStorage = true,
                    ExerciseType = dto.ExerciseType,
                    StartDate = dto.StartDate,
                    EndDate = dto.EndDate,
                    SortOrder = dto.SortOrder
                };
                dbContext.Set<CheckingExercise>().Add(existing);
                entity.CheckingExercises.Add(existing);
            }
            else
            {
                // #319: an exercise's dates are editable now, so an existing row has to take them.
                // Before the wizard captured them nothing could change an exercise's dates, and
                // this loop only ever reconciled datasets.
                dbContext.Entry(existing).CurrentValues.SetValues(new
                {
                    dto.ExerciseType,
                    dto.StartDate,
                    dto.EndDate,
                    dto.SortOrder
                });
            }

            existing.Name = dto.Name;
            existing.TabName = dto.TabName;
            existing.TabOrder = dto.TabOrder;
            existing.IsEnabled = dto.IsEnabled;
            existing.DisplayOnly = dto.DisplayOnly;
            existing.Layout = dto.Layout;
            existing.VisibleFrom = dto.VisibleFrom;
            existing.VisibleUntil = dto.VisibleUntil;
            existing.ReplacesCheckingExerciseId = dto.ReplacesCheckingExerciseId;
            existing.Validated = StampFor(dto);

            SyncDatasets(entity, existing, dto.Datasets);
        }

        foreach (CheckingExercise stale in entity.CheckingExercises
                     .Where(e => wanted.All(x => x.Id != e.Id && (x.Id != Guid.Empty || x.ExerciseType != e.ExerciseType)))
                     .ToList())
        {
            // A configured release is history: its blobs and its change requests point at this row.
            // Hide it instead of destroying it. A row the wizard never configured is still deleted.
            if (stale.TabName is not null) stale.IsEnabled = false;
            else entity.CheckingExercises.Remove(stale);
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

            existing.Required = dto.Required;
            existing.SortOrder = dto.SortOrder;
            existing.Included = dto.Included;
            existing.SourceFile = dto.SourceFile;
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
            // Set here and nowhere else: whether a slot feeds the journey is decided when it is
            // created (supplier slot or admin-added), and no update may change it.
            FeedsJourney = dto.FeedsJourney,
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
                Id = dto.Id == Guid.Empty ? Guid.NewGuid() : dto.Id,
                UsesExerciseStorage = true,
                Name = dto.Name,
                TabName = dto.TabName,
                TabOrder = dto.TabOrder,
                IsEnabled = dto.IsEnabled,
                DisplayOnly = dto.DisplayOnly,
                Layout = dto.Layout,
                VisibleFrom = dto.VisibleFrom,
                VisibleUntil = dto.VisibleUntil,
                ReplacesCheckingExerciseId = dto.ReplacesCheckingExerciseId,
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
                    UsesExerciseStorage = e.UsesExerciseStorage,
                    Name = e.Name,
                    TabName = e.TabName,
                    TabOrder = e.TabOrder,
                    IsEnabled = e.IsEnabled,
                    DisplayOnly = e.DisplayOnly,
                    VisibleFrom = e.VisibleFrom,
                    VisibleUntil = e.VisibleUntil,
                    WindowStart = entity.StartDate,
                    WindowEnd = entity.EndDate,
                    ReplacesCheckingExerciseId = e.ReplacesCheckingExerciseId,
                    CurrentReleaseId = e.CurrentReleaseId,
                    Layout = e.Layout,
                    // Oldest first. The school-facing display reads its schemas from the current release, and
                    // the admin exercise page lists every release so an earlier one can be made live again.
                    Releases = e.Releases
                        .OrderBy(r => r.Number)
                        .Select(r => new CheckingExerciseReleaseDto
                        {
                            Id = r.Id,
                            Number = r.Number,
                            PublishedAt = r.PublishedAt,
                            PublishedBy = r.PublishedBy,
                            FilesWritten = r.FilesWritten,
                            Files = r.Files
                                .OrderBy(f => f.SortOrder)
                                .Select(f => new CheckingExerciseReleaseFileDto
                                {
                                    DatasetId = f.DatasetId,
                                    DatasetName = f.DatasetName,
                                    FeedsJourney = f.FeedsJourney,
                                    Included = f.Included,
                                    SourceFile = f.SourceFile,
                                    IngressFile = f.IngressFile,
                                    IngressFileChecksum = f.IngressFileChecksum,
                                    SchemaFile = f.SchemaFile,
                                    SchemaFileChecksum = f.SchemaFileChecksum,
                                    SortOrder = f.SortOrder
                                })
                                .ToList()
                        })
                        .ToList(),
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
                            FeedsJourney = d.FeedsJourney,
                            SortOrder = d.SortOrder
                        })
                        .ToList()
                })
                .ToList()
        };
    }
}