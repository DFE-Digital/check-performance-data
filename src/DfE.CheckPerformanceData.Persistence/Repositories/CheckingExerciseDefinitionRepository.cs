using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Persistence.Repositories;

public sealed class CheckingExerciseDefinitionRepository(PortalDbContext db, IWindowRepository windows) : ICheckingExerciseDefinitionRepository
{
    public async Task<CheckingExerciseDefinition?> GetAsync(Guid exerciseId, CancellationToken cancellationToken)
    {
        var windowId = await db.Set<CheckingExercise>().Where(e => e.Id == exerciseId)
            .Select(e => (Guid?)e.CheckingWindowId).SingleOrDefaultAsync(cancellationToken);
        if (windowId is null) return null;
        var window = await windows.GetByIdAsync(windowId.Value, cancellationToken);
        return window is null ? null : new(window.Id, window.Exercises.Single(e => e.Id == exerciseId));
    }

    public async Task StampAsync(Guid exerciseId, DateTime validatedAt, string ingressChecksum, string schemaChecksum, CancellationToken cancellationToken)
    {
        var exercise = await db.Set<CheckingExercise>().SingleAsync(e => e.Id == exerciseId, cancellationToken);
        exercise.Validated = new ExerciseValidated
        {
            ValidatedAt = validatedAt,
            IngressValidationChecksum = ingressChecksum,
            SchemaValidationChecksum = schemaChecksum
        };
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<CheckingExerciseReleaseDto> PublishReleaseAsync(Guid exerciseId, CheckingExerciseReleaseDto release,
        string ingressChecksum, string schemaChecksum, CancellationToken cancellationToken)
    {
        var exercise = await db.Set<CheckingExercise>().SingleAsync(e => e.Id == exerciseId, cancellationToken);

        // The unique (exercise, number) index catches two runs that finish at the same moment:
        // the second save fails, and its output stays in storage without being made current.
        var number = await db.Set<CheckingExerciseRelease>()
            .Where(r => r.CheckingExerciseId == exerciseId)
            .Select(r => (int?)r.Number)
            .MaxAsync(cancellationToken) ?? 0;

        var entity = new CheckingExerciseRelease
        {
            Id = release.Id,
            CheckingExerciseId = exerciseId,
            Number = number + 1,
            // `timestamp without time zone`: Npgsql rejects a Utc kind. The instant is UTC.
            PublishedAt = DateTime.SpecifyKind(release.PublishedAt, DateTimeKind.Unspecified),
            PublishedBy = release.PublishedBy,
            FilesWritten = release.FilesWritten,
            Files = release.Files.Select(f => new CheckingExerciseReleaseFile
            {
                Id = Guid.NewGuid(),
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
            }).ToList()
        };
        db.Set<CheckingExerciseRelease>().Add(entity);

        // One save: the release row, the switch to it and the validation stamp. Schools see the
        // new output only after every one of its files is written, and never a release that has
        // no row.
        exercise.CurrentReleaseId = entity.Id;
        exercise.Validated = new ExerciseValidated
        {
            // This column is `timestamp with time zone`, which takes a Utc kind — unlike PublishedAt.
            ValidatedAt = DateTime.SpecifyKind(release.PublishedAt, DateTimeKind.Utc),
            IngressValidationChecksum = ingressChecksum,
            SchemaValidationChecksum = schemaChecksum
        };
        await db.SaveChangesAsync(cancellationToken);

        return new CheckingExerciseReleaseDto
        {
            Id = entity.Id,
            Number = entity.Number,
            PublishedAt = entity.PublishedAt,
            PublishedBy = entity.PublishedBy,
            FilesWritten = entity.FilesWritten,
            Files = release.Files
        };
    }

    public async Task<bool> SetCurrentReleaseAsync(Guid exerciseId, Guid releaseId, CancellationToken cancellationToken)
    {
        // CurrentReleaseId has no foreign key, so this check is what keeps it naming a release of
        // this exercise and not of another one.
        if (!await db.Set<CheckingExerciseRelease>()
                .AnyAsync(r => r.Id == releaseId && r.CheckingExerciseId == exerciseId, cancellationToken))
            return false;

        return await db.Set<CheckingExercise>()
            .Where(e => e.Id == exerciseId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.CurrentReleaseId, releaseId), cancellationToken) == 1;
    }
}
