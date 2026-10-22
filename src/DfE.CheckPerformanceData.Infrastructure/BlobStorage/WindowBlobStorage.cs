using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using DfE.CheckPerformanceData.Application.WindowManagement;

namespace DfE.CheckPerformanceData.Infrastructure.BlobStorage;

/// <inheritdoc cref="IWindowBlobStorage"/>
public sealed class WindowBlobStorage(BlobServiceClient blobServiceClient) : IWindowBlobStorage
{
    public async Task DeleteWindowContainerAsync(Guid windowId, CancellationToken cancellationToken) =>
        await blobServiceClient.GetBlobContainerClient(windowId.ToString())
            .DeleteIfExistsAsync(cancellationToken: cancellationToken);

    public async Task DeleteExerciseBlobsAsync(Guid windowId, Guid exerciseId, CancellationToken cancellationToken)
    {
        var container = blobServiceClient.GetBlobContainerClient(windowId.ToString());
        if (!await container.ExistsAsync(cancellationToken))
            return;

        foreach (var prefix in CheckingExerciseBlobPaths.OwnedPrefixes(exerciseId))
        {
            await foreach (var blob in container.GetBlobsAsync(BlobTraits.None, BlobStates.None, prefix, cancellationToken))
            {
                await container.DeleteBlobIfExistsAsync(blob.Name, cancellationToken: cancellationToken);
            }
        }
    }
}
