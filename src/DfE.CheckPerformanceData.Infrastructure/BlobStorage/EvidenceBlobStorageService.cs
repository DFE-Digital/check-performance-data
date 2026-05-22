using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Application.FileStorage;

namespace DfE.CheckPerformanceData.Infrastructure.BlobStorage;

public sealed class EvidenceBlobStorageService(BlobServiceClient blobServiceClient) : IFileStorageService
{
    private const string EvidenceFolder = "evidence-uploads";

    public async Task<string> SaveAsync(Guid windowId, byte[] bytes)
    {
        var container = blobServiceClient.GetBlobContainerClient(windowId.ToString());
        await container.CreateIfNotExistsAsync();

        var blobName = Guid.NewGuid().ToString();
        var blob = container.GetBlobClient($"{EvidenceFolder}/{blobName}");

        using var stream = new MemoryStream(bytes);
        await blob.UploadAsync(stream, overwrite: true);

        return blobName;
    }

    public async Task DeleteAsync(Guid windowId, string blobName)
    {
        var container = blobServiceClient.GetBlobContainerClient(windowId.ToString());
        var blob = container.GetBlobClient($"{EvidenceFolder}/{blobName}");
        await blob.DeleteIfExistsAsync();
    }
}
