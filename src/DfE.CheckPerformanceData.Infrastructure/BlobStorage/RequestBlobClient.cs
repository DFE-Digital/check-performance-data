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

    public async Task SaveRequestAsync(Guid windowId, string cypmdId, RequestDocument document)
    {
        var container = blobServiceClient.GetBlobContainerClient(windowId.ToString());
        await container.CreateIfNotExistsAsync();

        var json = JsonSerializer.Serialize(document, JsonOptions);
        var blob = container.GetBlobClient($"request_{cypmdId}.json");

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        await blob.UploadAsync(stream, overwrite: true);
    }
}
