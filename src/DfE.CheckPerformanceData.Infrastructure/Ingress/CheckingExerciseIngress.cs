using System.Runtime.CompilerServices;
using DfE.CheckPerformanceData.Application.WindowManagement;
using Microsoft.Extensions.Logging;

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
    ICsvSchemaFileProcessor processor, TimeProvider clock, IIngressSummaryStore? summaryStore = null,
    ILogger<CheckingExerciseIngress>? logger = null) : ICheckingExerciseIngress
{
    public async IAsyncEnumerable<ValidationProgress> ProcessAsync(Guid exerciseId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default, string publishedBy = "")
    {
        var definition = await definitions.GetAsync(exerciseId, cancellationToken);
        if (definition is null)
        {
            yield return new("Incomplete", "Supply a file and schema for every required ingress definition before processing.",
                0, 0, 0, 1, true, true);
            yield break;
        }

        var exercise = definition.Exercise;
        var toIngest = exercise.DatasetsToIngest;
        var inputs = toIngest.Select(ToIngressDataset).ToList();
        var captureInputs = exercise.HasRequiredFiles
            ? inputs
            : exercise.Datasets.Where(d => !d.Retired).OrderBy(d => d.SortOrder).Select(ToIngressDataset).ToList();
        var summaryCapture = new IngressSummaryCapture(definition.WindowId, exercise.Id, captureInputs,
            clock.GetUtcNow().UtcDateTime);

        if (summaryStore is not null &&
            !await summaryStore.ArchiveCurrentAsync(definition.WindowId, exercise.Id, cancellationToken))
        {
            yield return new("Failed", "Could not clear the previous ingress summary; import was not started.",
                0, 0, 0, 1, true, true);
            yield break;
        }

        if (captureInputs.Count == 0 || !exercise.HasRequiredFiles)
        {
            summaryCapture.SetFailure("configuration",
                captureInputs.Count == 0 ? "no_datasets" : "required_file_missing");
            await TryWriteSummaryAsync(summaryCapture, "failed", null, cancellationToken);
            yield return new("Incomplete", "Supply a file and schema for every required ingress definition before processing.",
                0, 0, 0, 1, true, true);
            yield break;
        }
        var ingressChecksum = exercise.CurrentIngressChecksum;
        var schemaChecksum = exercise.CurrentSchemaChecksum;

        // A legacy row (UsesExerciseStorage = false) keeps its kind-based paths and has no
        // releases: its readers do not know the release prefix.
        Guid? releaseId = exercise.UsesExerciseStorage ? Guid.NewGuid() : null;

        await using var enumerator = processor.ProcessAsync(definition.WindowId, exercise.ExerciseType, inputs,
            cancellationToken: cancellationToken,
            checkingExerciseId: exercise.UsesExerciseStorage ? exercise.Id : null, releaseId: releaseId,
            summaryCapture: summaryCapture).GetAsyncEnumerator(cancellationToken);
        while (true)
        {
            ValidationProgress? currentProgress = null;
            Exception? processingException = null;
            try
            {
                if (!await enumerator.MoveNextAsync())
                    break;
                currentProgress = enumerator.Current;
            }
            catch (OperationCanceledException ex)
            {
                processingException = ex;
            }
            catch (Exception ex)
            {
                processingException = ex;
            }

            if (processingException is not null)
            {
                var cancelled = processingException is OperationCanceledException;
                summaryCapture.SetFailure(cancelled ? "cancelled" : "input_read",
                    cancelled ? "operation_cancelled" : "input_read_failed");
                logger?.LogError("Ingress processing stopped ({ErrorCode}) for exercise {ExerciseId}",
                    cancelled ? "operation_cancelled" : "input_read_failed", exercise.Id);
                await TryWriteSummaryAsync(summaryCapture, cancelled ? "cancelled" : "failed", null,
                    cancelled ? CancellationToken.None : cancellationToken);
                yield return new(cancelled ? "Cancelled" : "Failed",
                    cancelled ? "Ingress was cancelled." : "Ingress processing failed.",
                    summaryCapture.RowsRead, 0, 0, 1, true, true);
                yield break;
            }

            var progress = currentProgress!;
            if (progress is { IsComplete: true, IsError: false })
            {
                var now = clock.GetUtcNow().UtcDateTime;
                ValidationProgress? publicationFailure = null;
                var failureOutcome = "failed";
                try
                {
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
                catch (OperationCanceledException)
                {
                    summaryCapture.SetFailure("cancelled", "operation_cancelled");
                    failureOutcome = "cancelled";
                    publicationFailure = new("Cancelled", "Ingress was cancelled.", progress.RecordsRead,
                        progress.RecordsProcessed, progress.FilesWritten, 1, true, true, progress.SchoolSummary);
                }
                catch
                {
                    summaryCapture.SetFailure("publication", "publication_failed");
                    publicationFailure = new("Failed", "Ingress publication failed.", progress.RecordsRead,
                        progress.RecordsProcessed, progress.FilesWritten, 1, true, true, progress.SchoolSummary);
                }

                if (publicationFailure is not null)
                {
                    await TryWriteSummaryAsync(summaryCapture, failureOutcome, null,
                        failureOutcome == "cancelled" ? CancellationToken.None : cancellationToken);
                    yield return publicationFailure;
                    yield break;
                }

                await TryWriteSummaryAsync(summaryCapture, "succeeded", releaseId, cancellationToken);
            }
            else if (progress.IsComplete)
            {
                if (summaryCapture.FailureCode is null)
                    summaryCapture.SetFailure("validation", "record_validation_failed");
                var outcome = summaryCapture.FailureStage == "cancelled" ? "cancelled" : "failed";
                await TryWriteSummaryAsync(summaryCapture, outcome, null, cancellationToken);
            }

            yield return progress;
        }
    }

    private static IngressDataset ToIngressDataset(CheckingWindowDatasetDto dataset) =>
        new(dataset.Name, dataset.IngressFile, dataset.IngressFileChecksum, dataset.SchemaFile,
            dataset.SchemaFileChecksum, dataset.Included, dataset.SourceFile, dataset.Id, dataset.FeedsJourney);

    private async Task TryWriteSummaryAsync(IngressSummaryCapture capture, string outcome, Guid? releaseId,
        CancellationToken cancellationToken)
    {
        if (summaryStore is null)
            return;

        try
        {
            var document = capture.Build(outcome, releaseId, clock.GetUtcNow().UtcDateTime);
            await summaryStore.WriteAsync(capture.WindowId, capture.ExerciseId, document, cancellationToken);
        }
        catch
        {
            logger?.LogError("Ingress summary write failed ({ErrorCode}) for exercise {ExerciseId}",
                "summary_write_failed", capture.ExerciseId);
        }
    }
}
