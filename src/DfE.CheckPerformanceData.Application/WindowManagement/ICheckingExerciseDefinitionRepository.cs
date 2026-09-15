namespace DfE.CheckPerformanceData.Application.WindowManagement;

public sealed record CheckingExerciseDefinition(Guid WindowId, CheckingExerciseDto Exercise);

public interface ICheckingExerciseDefinitionRepository
{
    Task<CheckingExerciseDefinition?> GetAsync(Guid exerciseId, CancellationToken cancellationToken);
    Task StampAsync(Guid exerciseId, DateTime validatedAt, string ingressChecksum, string schemaChecksum, CancellationToken cancellationToken);
}
