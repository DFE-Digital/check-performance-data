namespace DfE.CheckPerformanceData.Application.Egress;

/// <summary>
/// Where LDS picks files up: container "cypmd", folder "extracts_input" (AB#292610). The storage
/// ACCOUNT is the environment's ConnectionStrings:IngressStorage — the one account LDS both uploads
/// ingress files to and downloads egress files from. It is derived from the CYPMD
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

public interface IEgressBlobClient
{
    bool IsConfigured { get; }
    string TargetDescription { get; }
    /// <summary>Writes the file, replacing any same-named file, and stamps it with <paramref name="runId"/>.</summary>
    Task UploadAsync(string fileName, byte[] content, string sha256, Guid runId, CancellationToken ct);
    /// <summary>
    /// Deletes the blob only if it exists and its egressRunId metadata equals <paramref name="runId"/>;
    /// a blob that is absent or stamped with a different run is left untouched. Returns whether
    /// something was actually deleted (transfer compensation, a write whose success response was lost,
    /// and the M1 Abandon-during-Transferring sweep, without ever touching another run's file).
    /// </summary>
    Task<bool> DeleteIfOwnedByRunAsync(string fileName, Guid runId, CancellationToken ct);
}
