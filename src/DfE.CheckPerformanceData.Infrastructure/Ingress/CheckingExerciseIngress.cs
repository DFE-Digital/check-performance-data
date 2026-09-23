using System.Runtime.CompilerServices;
using DfE.CheckPerformanceData.Application.WindowManagement;

namespace DfE.CheckPerformanceData.Infrastructure.Ingress;

public interface ICheckingExerciseIngress
{
    IAsyncEnumerable<ValidationProgress> ProcessAsync(Guid exerciseId, bool clearExistingFiles = false,
        CancellationToken cancellationToken = default);
}

public sealed class CheckingExerciseIngress(ICheckingExerciseDefinitionRepository definitions,
    ICsvSchemaFileProcessor processor, TimeProvider clock) : ICheckingExerciseIngress
{
    public async IAsyncEnumerable<ValidationProgress> ProcessAsync(Guid exerciseId, bool clearExistingFiles = false,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var definition = await definitions.GetAsync(exerciseId, cancellationToken);
        if (definition is null || !definition.Exercise.HasRequiredFiles)
        {
            yield return new("Incomplete", "Supply a file and schema for every required ingress definition before processing.",
                0, 0, 0, 1, true, true);
            yield break;
        }
        var exercise = definition.Exercise;
        var inputs = exercise.DatasetsToIngest.Select(d => new IngressDataset(d.Name, d.IngressFile,
            d.IngressFileChecksum, d.SchemaFile, d.SchemaFileChecksum, d.Included, d.SourceFile)).ToList();
        var ingressChecksum = exercise.CurrentIngressChecksum;
        var schemaChecksum = exercise.CurrentSchemaChecksum;
        await foreach (var progress in processor.ProcessAsync(definition.WindowId, exercise.ExerciseType, inputs,
            clearExistingFiles: clearExistingFiles, cancellationToken: cancellationToken,
            checkingExerciseId: exercise.UsesExerciseStorage ? exercise.Id : null))
        {
            if (progress is { IsComplete: true, IsError: false })
                await definitions.StampAsync(exerciseId, clock.GetUtcNow().UtcDateTime, ingressChecksum, schemaChecksum, cancellationToken);
            yield return progress;
        }
    }
}
