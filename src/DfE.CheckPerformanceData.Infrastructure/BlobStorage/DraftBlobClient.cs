using System.Text;
using System.Text.Json;
using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.RequestSubmission;

namespace DfE.CheckPerformanceData.Infrastructure.BlobStorage;

public sealed class DraftBlobClient(BlobServiceClient blobServiceClient) : IDraftBlobClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task SaveDraftAsync(Guid windowId, string referenceNumber, RequestState state)
    {
        var container = blobServiceClient.GetBlobContainerClient(windowId.ToString());
        await container.CreateIfNotExistsAsync();

        var blob = container.GetBlobClient($"draft_requests/{referenceNumber}.json");
        var json = JsonSerializer.Serialize(state, JsonOptions);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        await blob.UploadAsync(stream, overwrite: true);
    }

    public async Task<RequestState?> GetDraftAsync(Guid windowId, string referenceNumber)
    {
        var container = blobServiceClient.GetBlobContainerClient(windowId.ToString());
        var blob = container.GetBlobClient($"draft_requests/{referenceNumber}.json");

        if (!await blob.ExistsAsync())
            return null;

        var response = await blob.DownloadContentAsync();
        return JsonSerializer.Deserialize<RequestState>(response.Value.Content, JsonOptions);
    }
}
