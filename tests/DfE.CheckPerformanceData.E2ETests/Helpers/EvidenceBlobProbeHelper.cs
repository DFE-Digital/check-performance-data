using Azure.Storage.Blobs;

namespace DfE.CheckPerformanceData.E2ETests.Helpers;

public static class EvidenceBlobProbeHelper
{
    public static async Task<long?> TryCountEvidenceAsync(Guid windowId)
    {
        var connectionString = Environment.GetEnvironmentVariable("CPD_E2E_AZURE_STORAGE");
        if (string.IsNullOrWhiteSpace(connectionString)) return null;

        try
        {
            var container = new BlobServiceClient(connectionString)
                .GetBlobContainerClient(windowId.ToString());
            long count = 0;
            await foreach (var _ in container.GetBlobsAsync(
                               Azure.Storage.Blobs.Models.BlobTraits.None,
                               Azure.Storage.Blobs.Models.BlobStates.None,
                               "evidence-uploads/",
                               CancellationToken.None)) count++;
            return count;
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            return 0;
        }
        catch (Azure.RequestFailedException ex) when (ex.Status is 400 or 403 or 404 or 500 or 503)
        {
            return null;
        }
    }
}
