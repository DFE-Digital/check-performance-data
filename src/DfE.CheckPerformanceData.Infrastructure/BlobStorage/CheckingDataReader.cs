using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Application.WindowManagement;

namespace DfE.CheckPerformanceData.Infrastructure.BlobStorage;

public sealed class CheckingDataReader(BlobServiceClient blobs) : ICheckingDataReader
{
    public async Task<byte[]?> ReadAsync(CheckingDataExercise exercise, string laestab, CancellationToken cancellationToken)
    {
        // Window/type is unique in the database; separate release windows never share output.
        var blob = blobs.GetBlobContainerClient(exercise.WindowId.ToString())
            .GetBlobClient(exercise.UsesExerciseStorage
                ? CheckingExerciseBlobPaths.DataBlobName(exercise.Id, CheckingExerciseBlobPaths.DefaultDataType(exercise.ExerciseType), laestab)
                : CheckingExerciseBlobPaths.DataBlobName(exercise.ExerciseType!.Value, laestab));
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
