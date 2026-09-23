namespace DfE.CheckPerformanceData.Application.WindowManagement;

/// <summary>One exercise and the window it belongs to, addressed by the exercise's own id.</summary>
public sealed record CheckingExerciseDefinition(Guid WindowId, CheckingExerciseDto Exercise);

/// <summary>
/// Reads and stamps one exercise. Separate from <see cref="IWindowRepository"/> because an ingress
/// run knows only the exercise it was asked to run, never which window holds it.
/// </summary>
public interface ICheckingExerciseDefinitionRepository
{
    Task<CheckingExerciseDefinition?> GetAsync(Guid exerciseId, CancellationToken cancellationToken);

    Task StampAsync(Guid exerciseId, DateTime validatedAt, string ingressChecksum,
        string schemaChecksum, CancellationToken cancellationToken);
}
