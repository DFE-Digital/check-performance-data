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

    public async Task<byte[]?> ReadSchemaAsync(Guid windowId, string schemaFile, CancellationToken cancellationToken)
    {
        var blob = blobs.GetBlobContainerClient(windowId.ToString())
            .GetBlobClient(CheckingExerciseBlobPaths.SchemaBlobName(schemaFile));
        if (!await blob.ExistsAsync(cancellationToken)) return null;
        return (await blob.DownloadContentAsync(cancellationToken)).Value.Content.ToArray();
    }
}
