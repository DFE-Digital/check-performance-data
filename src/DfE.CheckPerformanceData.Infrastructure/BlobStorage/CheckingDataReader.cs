using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Application.WindowManagement;

namespace DfE.CheckPerformanceData.Infrastructure.BlobStorage;

public sealed class CheckingDataReader(BlobServiceClient blobs) : ICheckingDataReader
{
    public async Task<byte[]?> ReadAsync(CheckingDataExercise exercise, string laestab, CancellationToken cancellationToken)
    {
        // The exercise's current release names the output schools see.
        var blob = blobs.GetBlobContainerClient(exercise.WindowId.ToString())
            .GetBlobClient(exercise.UsesExerciseStorage
                ? CheckingExerciseBlobPaths.DataBlobName(exercise.Id,
                    CheckingExerciseBlobPaths.DefaultDataType(exercise.ExerciseType), laestab, exercise.CurrentReleaseId)
                : CheckingExerciseBlobPaths.DataBlobName(exercise.ExerciseType!.Value, laestab));
        if (!await blob.ExistsAsync(cancellationToken)) return null;
        return (await blob.DownloadContentAsync(cancellationToken)).Value.Content.ToArray();
    }

    public async Task<byte[]?> ReadDatasetAsync(CheckingDataExercise exercise, Guid datasetId, string laestab,
        CancellationToken cancellationToken)
    {
        // Only a release has per-dataset files. Output from before releases is one merged file.
        if (exercise.CurrentReleaseId is not { } releaseId || !exercise.UsesExerciseStorage) return null;
        var blob = blobs.GetBlobContainerClient(exercise.WindowId.ToString())
            .GetBlobClient(CheckingExerciseBlobPaths.DatasetBlobName(exercise.Id, releaseId, datasetId, laestab));
        if (!await blob.ExistsAsync(cancellationToken)) return null;
        return (await blob.DownloadContentAsync(cancellationToken)).Value.Content.ToArray();
    }

    public async Task<bool> HasSchoolDataAsync(Guid windowId, CheckingExerciseDto exercise, string laestab,
        CancellationToken cancellationToken)
    {
        var container = blobs.GetBlobContainerClient(windowId.ToString());

        if (!exercise.UsesExerciseStorage)
            return exercise.ExerciseType is { } kind
                && await container.GetBlobClient(CheckingExerciseBlobPaths.DataBlobName(kind, laestab))
                    .ExistsAsync(cancellationToken);

        // The merged file holds only the journey slots, so a data share has none. Its data is in
        // the per-dataset files of the current release.
        var merged = CheckingExerciseBlobPaths.DataBlobName(exercise.Id,
            CheckingExerciseBlobPaths.DefaultDataType(exercise.ExerciseType), laestab, exercise.CurrentReleaseId);
        if (await container.GetBlobClient(merged).ExistsAsync(cancellationToken)) return true;

        if (exercise.CurrentReleaseId is not { } releaseId) return false;
        foreach (var dataset in exercise.PublishedDatasets)
        {
            var name = CheckingExerciseBlobPaths.DatasetBlobName(exercise.Id, releaseId, dataset.Id, laestab);
            if (await container.GetBlobClient(name).ExistsAsync(cancellationToken)) return true;
        }
        return false;
    }

    public async Task<byte[]?> ReadSchemaAsync(Guid windowId, string schemaFile, CancellationToken cancellationToken)
    {
        var blob = blobs.GetBlobContainerClient(windowId.ToString())
            .GetBlobClient(CheckingExerciseBlobPaths.SchemaBlobName(schemaFile));
        if (!await blob.ExistsAsync(cancellationToken)) return null;
        return (await blob.DownloadContentAsync(cancellationToken)).Value.Content.ToArray();
    }
}
