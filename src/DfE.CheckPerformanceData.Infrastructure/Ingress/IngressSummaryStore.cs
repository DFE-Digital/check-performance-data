using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Application.WindowManagement;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace DfE.CheckPerformanceData.Infrastructure.Ingress;

public sealed class IngressSummaryStore(
    IReadOnlyDictionary<string, BlobServiceClient> blobClients,
    ILogger<IngressSummaryStore> logger) : IIngressSummaryStore
{
    public async Task<IngressSummaryDocument?> GetAsync(Guid windowId, Guid exerciseId,
        CancellationToken cancellationToken)
    {
        if (!blobClients.TryGetValue("app", out var blobServiceClient))
            throw new InvalidOperationException("Ingress summary storage is unavailable.");

        var blob = blobServiceClient.GetBlobContainerClient(windowId.ToString())
            .GetBlobClient(CheckingExerciseBlobPaths.IngressSummaryBlobName(exerciseId));
        if (!await blob.ExistsAsync(cancellationToken)) return null;

        var content = await blob.DownloadContentAsync(cancellationToken);
        return JsonConvert.DeserializeObject<IngressSummaryDocument>(content.Value.Content.ToString());
    }

    public async Task<bool> ArchiveCurrentAsync(Guid windowId, Guid exerciseId, CancellationToken cancellationToken)
    {
        if (!blobClients.TryGetValue("app", out var blobServiceClient))
        {
            logger.LogError("Ingress summary storage is unavailable ({ErrorCode}) for exercise {ExerciseId}",
                "summary_clear_failed", exerciseId);
            return false;
        }

        var container = blobServiceClient.GetBlobContainerClient(windowId.ToString());
        var current = container.GetBlobClient(CheckingExerciseBlobPaths.IngressSummaryBlobName(exerciseId));
        try
        {
            if (!await current.ExistsAsync(cancellationToken))
                return true;

            var content = await current.DownloadContentAsync(cancellationToken);
            var archiveName = CheckingExerciseBlobPaths.IngressSummaryHistoryPrefix(exerciseId) +
                              $"ingress_summary_{DateTime.UtcNow:yyyyMMdd_HHmmss_fffffff}_{Guid.NewGuid():N}.json";
            var archive = container.GetBlobClient(archiveName);
            await archive.UploadAsync(content.Value.Content, overwrite: false, cancellationToken);
            await current.DeleteIfExistsAsync(cancellationToken: cancellationToken);
            return true;
        }
        catch
        {
            logger.LogError("Ingress summary archival failed ({ErrorCode}) for exercise {ExerciseId}",
                "summary_clear_failed", exerciseId);
            return false;
        }
    }

    public async Task<bool> WriteAsync(Guid windowId, Guid exerciseId, IngressSummaryDocument summary,
        CancellationToken cancellationToken)
    {
        if (!blobClients.TryGetValue("app", out var blobServiceClient))
        {
            logger.LogError("Ingress summary storage is unavailable ({ErrorCode}) for exercise {ExerciseId}",
                "summary_write_failed", exerciseId);
            return false;
        }

        try
        {
            var container = blobServiceClient.GetBlobContainerClient(windowId.ToString());
            var blob = container.GetBlobClient(CheckingExerciseBlobPaths.IngressSummaryBlobName(exerciseId));
            var json = JsonConvert.SerializeObject(summary, Formatting.Indented);
            await blob.UploadAsync(BinaryData.FromString(json), overwrite: true, cancellationToken);
            return true;
        }
        catch
        {
            logger.LogError("Ingress summary write failed ({ErrorCode}) for exercise {ExerciseId}",
                "summary_write_failed", exerciseId);
            return false;
        }
    }
}
