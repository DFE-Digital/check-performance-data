using System.Text;
using System.Text.Json;
using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.RequestSubmission;

namespace DfE.CheckPerformanceData.Infrastructure.BlobStorage;

public sealed class RequestStateBlobClient(BlobServiceClient blobServiceClient) : IRequestStateBlobClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static string BlobName(string referenceNumber) => $"requests/{referenceNumber}.json";

    public async Task SaveAsync(Guid windowId, string referenceNumber, RequestState state)
    {
        var container = blobServiceClient.GetBlobContainerClient(windowId.ToString());
        await container.CreateIfNotExistsAsync();

        var blob = container.GetBlobClient(BlobName(referenceNumber));
        var json = JsonSerializer.Serialize(state, JsonOptions);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        await blob.UploadAsync(stream, overwrite: true);
    }

    public async Task<RequestState?> GetAsync(Guid windowId, string referenceNumber)
    {
        var container = blobServiceClient.GetBlobContainerClient(windowId.ToString());
        var blob = container.GetBlobClient(BlobName(referenceNumber));

        if (!await blob.ExistsAsync())
            return null;

        var response = await blob.DownloadContentAsync();
        return JsonSerializer.Deserialize<RequestState>(response.Value.Content, JsonOptions);
    }
}
