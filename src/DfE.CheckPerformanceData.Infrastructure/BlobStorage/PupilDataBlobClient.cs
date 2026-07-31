using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Infrastructure.BlobStorage;

public sealed class PupilDataBlobClient(BlobServiceClient blobServiceClient) : IPupilDataBlobClient
{
    // Public so the pupil-schema deserialization tests bind supplier JSON exactly as production does.
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(), new NullToEmptyStringJsonConverter() }
    };

    /// <summary>The concrete deserialisation target for a window type. Post16 has its own
    /// supplier schema (FORENAMES, ULN, stamped INCLUDED); every other type uses the KS4 shape.</summary>
    public static Type RecordTypeFor(CheckingWindowType windowType)
        => windowType == CheckingWindowType.Post16 ? typeof(Post16PupilRecord) : typeof(PupilRecord);

    // Public so the pupil-schema tests bind supplier JSON exactly as production does.
    public static IReadOnlyList<IPupilRecord> Deserialize(ReadOnlySpan<byte> utf8Json, CheckingWindowType windowType)
        => windowType == CheckingWindowType.Post16
            ? JsonSerializer.Deserialize<List<Post16PupilRecord>>(utf8Json, JsonOptions) ?? []
            : JsonSerializer.Deserialize<List<PupilRecord>>(utf8Json, JsonOptions) ?? [];

    public async Task<IReadOnlyList<IPupilRecord>?> GetPupilsAsync(Guid windowId, string laestab, CheckingWindowType windowType)
    {
        var blob = GetBlobClient(windowId, laestab);

        if (!await blob.ExistsAsync())
            return null;

        var response = await blob.DownloadContentAsync();
        // Malformed JSON intentionally throws so corrupt files surface rather than read as empty.
        return Deserialize(response.Value.Content.ToMemory().Span, windowType);
    }

    public async Task<bool> HasPupilDataAsync(Guid windowId, string laestab)
        => await GetBlobClient(windowId, laestab).ExistsAsync();

    public async Task<IReadOnlyList<string>> ListSchoolLaestabsAsync(Guid windowId)
    {
        var container = blobServiceClient.GetBlobContainerClient(windowId.ToString());
        if (!await container.ExistsAsync())
            return [];

        const string prefix = "data/";
        const string suffix = "_pupils.json";
        var laestabs = new List<string>();
        await foreach (var blob in container.GetBlobsAsync(BlobTraits.None, BlobStates.None, prefix, CancellationToken.None))
        {
            if (blob.Name.EndsWith(suffix, StringComparison.Ordinal))
                laestabs.Add(blob.Name[prefix.Length..^suffix.Length]);
        }
        return laestabs;
    }

    public async Task UploadPupilsAsync<T>(Guid windowId, string laestab, List<T> pupils) where T : IPupilRecord
    {
        var container = blobServiceClient.GetBlobContainerClient(windowId.ToString());
        await container.CreateIfNotExistsAsync();

        var blob = container.GetBlobClient(BlobName(laestab));
        // Serialise against the runtime type so each record's own [JsonPropertyName] map is used.
        var json = JsonSerializer.Serialize<List<T>>(pupils, JsonOptions);
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
