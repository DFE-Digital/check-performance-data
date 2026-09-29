using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Application.WindowManagement;

namespace DfE.CheckPerformanceData.Infrastructure.BlobStorage;

/// <inheritdoc cref="IWindowBlobStorage"/>
public sealed class WindowBlobStorage(BlobServiceClient blobServiceClient) : IWindowBlobStorage
{
    public async Task DeleteWindowContainerAsync(Guid windowId, CancellationToken cancellationToken) =>
        await blobServiceClient.GetBlobContainerClient(windowId.ToString())
            .DeleteIfExistsAsync(cancellationToken: cancellationToken);
}
