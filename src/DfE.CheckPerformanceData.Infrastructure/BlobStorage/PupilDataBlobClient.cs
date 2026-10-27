using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Dashboard;
using DfE.CheckPerformanceData.Application.WindowManagement;
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

    public async Task<IReadOnlyList<IPupilRecord>?> GetPupilsAsync(
        Guid windowId, CheckingExerciseType exercise, string laestab, CheckingWindowType windowType)
    {
        var blob = GetBlobClient(windowId, exercise, laestab);

        if (!await blob.ExistsAsync())
            return null;

        var response = await blob.DownloadContentAsync();
        // Malformed JSON intentionally throws so corrupt files surface rather than read as empty.
        return Deserialize(response.Value.Content.ToMemory().Span, windowType);
    }

    public async Task<bool> HasPupilDataAsync(Guid windowId, CheckingExerciseType exercise, string laestab)
        => await GetBlobClient(windowId, exercise, laestab).ExistsAsync();

    public async Task<IReadOnlyList<string>> ListSchoolLaestabsAsync(
        Guid windowId, CheckingExerciseType exercise, CancellationToken cancellationToken = default)
    {
        var container = blobServiceClient.GetBlobContainerClient(windowId.ToString());
        if (!await container.ExistsAsync(cancellationToken))
            return [];

        string prefix = CheckingExerciseBlobPaths.DataPrefix(exercise);
        const string suffix = CheckingExerciseBlobPaths.PupilsSuffix;
        var laestabs = new HashSet<string>(StringComparer.Ordinal);
        await foreach (var blob in container.GetBlobsAsync(BlobTraits.None, BlobStates.None, prefix, cancellationToken))
        {
            if (!blob.Name.EndsWith(suffix, StringComparison.Ordinal))
                continue;

            // Normalise rather than trusting the blob name: the ingress writes the supplier's
            // LAESTAB column through verbatim, so anything other than a clean digit string
            // would silently fail to join against the digits-only laestab on a login row.
            var laestab = LaestabNormaliser.Normalise(blob.Name[prefix.Length..^suffix.Length]);
            if (laestab.Length > 0)
                laestabs.Add(laestab);
        }
        return laestabs.ToList();
    }

    public async Task UploadPupilsAsync<T>(
        Guid windowId, CheckingExerciseType exercise, string laestab, List<T> pupils) where T : IPupilRecord
    {
        var container = blobServiceClient.GetBlobContainerClient(windowId.ToString());
        await container.CreateIfNotExistsAsync();

        var blob = container.GetBlobClient(CheckingExerciseBlobPaths.PupilsBlobName(exercise, laestab));
        // Serialise against the runtime type so each record's own [JsonPropertyName] map is used.
        var json = JsonSerializer.Serialize<List<T>>(pupils, JsonOptions);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        await blob.UploadAsync(stream, overwrite: true);
    }

    // The layout lives in CheckingExerciseBlobPaths and nowhere else, so a prefix change cannot
    // leave the reader and the ingress writer disagreeing about where a school's file is.
    private BlobClient GetBlobClient(Guid windowId, CheckingExerciseType exercise, string laestab)
        => blobServiceClient.GetBlobContainerClient(windowId.ToString())
            .GetBlobClient(CheckingExerciseBlobPaths.PupilsBlobName(exercise, laestab));
}
