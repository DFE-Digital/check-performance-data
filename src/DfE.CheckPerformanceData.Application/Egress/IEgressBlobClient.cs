namespace DfE.CheckPerformanceData.Application.Egress;

/// <summary>
/// Where LDS picks files up: container "cypmd", folder "extracts_input" (AB#292610). The storage
/// ACCOUNT is the environment's ConnectionStrings:EgressStorage — derived from the CYPMD
/// environment's configuration, never chosen by the user or edited at runtime (AB#294553
/// "Transfer"). These two names are bindable only so a test can point at a scratch container.
/// </summary>
public sealed class EgressStorageOptions
{
    public const string SectionName = "EgressStorage";
    public string Container { get; set; } = "cypmd";
    public string Prefix { get; set; } = "extracts_input/";
    public string TargetDescription => $"{Container}/{Prefix.TrimEnd('/')}";
}

public sealed class EgressBlobAlreadyExistsException(string blobName) : Exception($"A file named {blobName} already exists in the LDS container.");

public interface IEgressBlobClient
{
    bool IsConfigured { get; }
    string TargetDescription { get; }
    /// <exception cref="EgressBlobAlreadyExistsException">A blob with that name exists — never overwritten.</exception>
    Task UploadAsync(string fileName, byte[] content, string sha256, Guid runId, CancellationToken ct);
    Task DeleteIfExistsAsync(string fileName, CancellationToken ct);
}
