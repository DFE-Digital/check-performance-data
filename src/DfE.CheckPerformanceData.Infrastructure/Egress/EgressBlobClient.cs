using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using DfE.CheckPerformanceData.Application.Egress;
using Microsoft.Extensions.Options;

namespace DfE.CheckPerformanceData.Infrastructure.Egress;

/// <summary>Uploads egress files to the LDS account's cypmd/extracts_input, never overwriting.</summary>
public sealed class EgressBlobClient(IReadOnlyDictionary<string, BlobServiceClient> clients, IOptions<EgressStorageOptions> options) : IEgressBlobClient
{
    public const string ClientKey = "egress";

    public bool IsConfigured => clients.ContainsKey(ClientKey);
    public string TargetDescription => options.Value.TargetDescription;

    public async Task UploadAsync(string fileName, byte[] content, string sha256, Guid runId, CancellationToken ct)
    {
        var container = clients[ClientKey].GetBlobContainerClient(options.Value.Container);
        await container.CreateIfNotExistsAsync(cancellationToken: ct);
        var blob = container.GetBlobClient(options.Value.Prefix + fileName);
        try
        {
            await blob.UploadAsync(new BinaryData(content), new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = "text/csv" },
                Metadata = new Dictionary<string, string> { ["sha256"] = sha256, ["egressRunId"] = runId.ToString() },
                // IfNoneMatch "*" = create only. The same window and type can never be sent twice,
                // and a leftover from a failed compensation must be looked at, not silently replaced.
                Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All }
            }, ct);
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            throw new EgressBlobAlreadyExistsException(options.Value.Prefix + fileName);
        }
    }

    public async Task DeleteIfExistsAsync(string fileName, CancellationToken ct)
    {
        var container = clients[ClientKey].GetBlobContainerClient(options.Value.Container);
        await container.GetBlobClient(options.Value.Prefix + fileName).DeleteIfExistsAsync(cancellationToken: ct);
    }
}
