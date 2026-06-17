using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;

namespace DfE.CheckPerformanceData.Infrastructure.BlobStorage;

public sealed class PupilDataBlobClient(BlobServiceClient blobServiceClient) : IPupilDataBlobClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<IReadOnlyList<PupilRecord>?> GetPupilsAsync(Guid windowId, string laestab)
    {
        var blob = GetBlobClient(windowId, laestab);

        if (!await blob.ExistsAsync())
            return null;

        var response = await blob.DownloadContentAsync();
        // Malformed JSON intentionally throws so corrupt files surface rather than read as empty.
        return JsonSerializer.Deserialize<List<PupilRecord>>(response.Value.Content, JsonOptions);
    }

    public async Task<bool> HasPupilDataAsync(Guid windowId, string laestab)
        => await GetBlobClient(windowId, laestab).ExistsAsync();

    public async Task UploadPupilsAsync(Guid windowId, string laestab, IReadOnlyList<PupilRecord> pupils)
    {
        var container = blobServiceClient.GetBlobContainerClient(windowId.ToString());
        await container.CreateIfNotExistsAsync();

        var blob = container.GetBlobClient(BlobName(laestab));
        var json = JsonSerializer.Serialize(pupils, JsonOptions);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        await blob.UploadAsync(stream, overwrite: true);
    }

    private BlobClient GetBlobClient(Guid windowId, string laestab)
        => blobServiceClient.GetBlobContainerClient(windowId.ToString()).GetBlobClient(BlobName(laestab));

    // laestab e.g. "933/4290" -> "data/9334290_pupils.json"; the slash is stripped so the
    // blob name has a single virtual "data/" folder rather than nesting on the laestab.
    private static string BlobName(string laestab)
        => $"data/{laestab.Replace("/", string.Empty)}_pupils.json";
}
