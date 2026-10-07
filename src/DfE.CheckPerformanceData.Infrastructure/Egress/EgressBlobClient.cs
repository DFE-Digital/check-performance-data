using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using DfE.CheckPerformanceData.Application.Egress;
using Microsoft.Extensions.Options;

namespace DfE.CheckPerformanceData.Infrastructure.Egress;

/// <summary>Uploads egress files to the LDS account's cypmd/extracts_input, replacing a same-named file.</summary>
public sealed class EgressBlobClient(IReadOnlyDictionary<string, BlobServiceClient> clients, IOptions<EgressStorageOptions> options) : IEgressBlobClient
{
    /// <summary>The ingress account: LDS uploads ingress files to it and downloads egress files from it.</summary>
    public const string ClientKey = "ingress";

    public bool IsConfigured => clients.ContainsKey(ClientKey);
    public string TargetDescription => options.Value.TargetDescription;

    public async Task UploadAsync(string fileName, byte[] content, string sha256, Guid runId, CancellationToken ct)
    {
        var container = clients[ClientKey].GetBlobContainerClient(options.Value.Container);
        await container.CreateIfNotExistsAsync(cancellationToken: ct);
        // No create-only condition: a same-named file (an earlier run of the same key stage on the
        // same day, or this run's own earlier attempt) is replaced, so the newest transfer wins.
        await Blob(fileName).UploadAsync(new BinaryData(content), new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = "text/csv" },
            Metadata = new Dictionary<string, string> { ["sha256"] = sha256, ["egressRunId"] = runId.ToString() }
        }, ct);
    }

    public async Task<bool> DeleteIfOwnedByRunAsync(string fileName, Guid runId, CancellationToken ct)
    {
        if (await OwnerAsync(fileName, ct) != runId) return false;
        await Blob(fileName).DeleteIfExistsAsync(cancellationToken: ct);
        return true;
    }

    private BlobClient Blob(string fileName) =>
        clients[ClientKey].GetBlobContainerClient(options.Value.Container).GetBlobClient(options.Value.Prefix + fileName);

    private async Task<Guid?> OwnerAsync(string fileName, CancellationToken ct)
    {
        BlobProperties properties;
        try
        {
            properties = await Blob(fileName).GetPropertiesAsync(cancellationToken: ct);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
        return properties.Metadata.TryGetValue("egressRunId", out var owner) && Guid.TryParse(owner, out var id) ? id : null;
    }
}
