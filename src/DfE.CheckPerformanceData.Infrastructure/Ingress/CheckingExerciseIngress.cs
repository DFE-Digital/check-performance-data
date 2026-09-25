using System.Runtime.CompilerServices;
using DfE.CheckPerformanceData.Application.WindowManagement;

namespace DfE.CheckPerformanceData.Infrastructure.Ingress;

public interface ICheckingExerciseIngress
{
    /// <summary>
    /// Runs one exercise's datasets. On an exercise on the exercise-id storage, a clean run is a
    /// new release: its output goes under its own prefix, and the exercise switches to it only
    /// after every file is written. The earlier releases stay in storage.
    /// </summary>
    /// <param name="publishedBy">Who started the run, recorded on the release.</param>
    IAsyncEnumerable<ValidationProgress> ProcessAsync(Guid exerciseId,
        CancellationToken cancellationToken = default, string publishedBy = "");
}

public sealed class CheckingExerciseIngress(ICheckingExerciseDefinitionRepository definitions,
    ICsvSchemaFileProcessor processor, TimeProvider clock) : ICheckingExerciseIngress
{
    public async IAsyncEnumerable<ValidationProgress> ProcessAsync(Guid exerciseId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default, string publishedBy = "")
    {
        var definition = await definitions.GetAsync(exerciseId, cancellationToken);
        if (definition is null || !definition.Exercise.HasRequiredFiles)
        {
            yield return new("Incomplete", "Supply a file and schema for every required ingress definition before processing.",
                0, 0, 0, 1, true, true);
            yield break;
        }
        var exercise = definition.Exercise;
        var toIngest = exercise.DatasetsToIngest;
        var inputs = toIngest.Select(d => new IngressDataset(d.Name, d.IngressFile,
            d.IngressFileChecksum, d.SchemaFile, d.SchemaFileChecksum, d.Included, d.SourceFile,
            d.Id, d.FeedsJourney)).ToList();
        var ingressChecksum = exercise.CurrentIngressChecksum;
        var schemaChecksum = exercise.CurrentSchemaChecksum;

        // A legacy row (UsesExerciseStorage = false) keeps its kind-based paths and has no
        // releases: its readers do not know the release prefix.
        Guid? releaseId = exercise.UsesExerciseStorage ? Guid.NewGuid() : null;

        await foreach (var progress in processor.ProcessAsync(definition.WindowId, exercise.ExerciseType, inputs,
            cancellationToken: cancellationToken,
            checkingExerciseId: exercise.UsesExerciseStorage ? exercise.Id : null, releaseId: releaseId))
        {
            if (progress is { IsComplete: true, IsError: false })
            {
                var now = clock.GetUtcNow().UtcDateTime;
                if (releaseId is { } id)
                {
                    await definitions.PublishReleaseAsync(exerciseId, new CheckingExerciseReleaseDto
                    {
                        Id = id,
                        PublishedAt = now,
                        PublishedBy = publishedBy,
                        FilesWritten = progress.FilesWritten,
                        Files = [.. toIngest.Select(d => new CheckingExerciseReleaseFileDto
                        {
                            DatasetId = d.Id,
                            DatasetName = d.Name,
                            FeedsJourney = d.FeedsJourney,
                            Included = d.Included,
                            SourceFile = d.SourceFile,
                            IngressFile = d.IngressFile,
                            IngressFileChecksum = d.IngressFileChecksum,
                            SchemaFile = d.SchemaFile,
                            SchemaFileChecksum = d.SchemaFileChecksum,
                            SortOrder = d.SortOrder
                        })]
                    }, ingressChecksum, schemaChecksum, cancellationToken);
                }
                else
                {
                    await definitions.StampAsync(exerciseId, now, ingressChecksum, schemaChecksum, cancellationToken);
                }
            }
            yield return progress;
        }
    }
}
