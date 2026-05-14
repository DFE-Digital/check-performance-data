using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Application.BlobStorage;

namespace DfE.CheckPerformanceData.Infrastructure.BlobStorage;

public sealed class BlobContainerService(BlobServiceClient blobServiceClient) : IBlobContainerService
{
    public async Task EnsureContainerExistsAsync(Guid windowId)
    {
        var container = blobServiceClient.GetBlobContainerClient(windowId.ToString());
        await container.CreateIfNotExistsAsync();
    }
}
