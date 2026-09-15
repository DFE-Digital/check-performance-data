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
}
