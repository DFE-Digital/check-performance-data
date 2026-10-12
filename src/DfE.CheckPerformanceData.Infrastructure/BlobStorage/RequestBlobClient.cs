using System.Text;
using System.Text.Json;
using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Application.RequestSubmission;

namespace DfE.CheckPerformanceData.Infrastructure.BlobStorage;

public sealed class RequestBlobClient(BlobServiceClient blobServiceClient) : IRequestBlobClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private static string BlobName(string referenceNumber) => $"request_{referenceNumber}.json";

    public async Task SaveRequestAsync(Guid windowId, RequestDocument document)
    {
        var container = blobServiceClient.GetBlobContainerClient(windowId.ToString());
        await container.CreateIfNotExistsAsync();

        var json = JsonSerializer.Serialize(document, JsonOptions);
        var blob = container.GetBlobClient(BlobName(document.ReferenceNumber));

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        await blob.UploadAsync(stream, overwrite: true);
    }

    public async Task<RequestDocument?> GetRequestAsync(Guid windowId, string referenceNumber)
    {
        var blob = blobServiceClient
            .GetBlobContainerClient(windowId.ToString())
            .GetBlobClient(BlobName(referenceNumber));

        if (!await blob.ExistsAsync())
            return null;

        var response = await blob.DownloadContentAsync();
        return JsonSerializer.Deserialize<RequestDocument>(response.Value.Content, JsonOptions);
    }
}
