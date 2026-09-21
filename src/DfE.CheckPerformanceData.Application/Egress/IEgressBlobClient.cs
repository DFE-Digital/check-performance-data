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

public sealed class EgressBlobAlreadyExistsException(string blobName, string? detail = null)
    : Exception(detail is null ? Sentence(blobName) : $"{Sentence(blobName)} {detail}")
{
    public string BlobName { get; } = blobName;
    private static string Sentence(string blobName) => $"A file named {blobName} already exists in the LDS container.";
}

public interface IEgressBlobClient
{
    bool IsConfigured { get; }
    string TargetDescription { get; }
    /// <exception cref="EgressBlobAlreadyExistsException">A blob with that name exists — never overwritten.</exception>
    Task UploadAsync(string fileName, byte[] content, string sha256, Guid runId, CancellationToken ct);
    Task DeleteIfExistsAsync(string fileName, CancellationToken ct);
    /// <summary>
    /// Deletes the blob only if it exists and its egressRunId metadata equals <paramref name="runId"/>;
    /// a blob that is absent or stamped with a different run is left untouched. Returns whether
    /// something was actually deleted (M1/S3: cleans up a write whose success response was lost,
    /// and the M1 Abandon-during-Transferring sweep, without ever touching another run's file).
    /// </summary>
    Task<bool> DeleteIfOwnedByRunAsync(string fileName, Guid runId, CancellationToken ct);
    /// <summary>
    /// The egressRunId stamped on an existing blob, or null when the blob is absent or carries no
    /// such stamp (a file this service never wrote). Lets a transfer that collides with a
    /// same-named file decide whether it is reclaiming its own leftover, an abandoned run's
    /// orphan, or colliding with a real file that must be left alone.
    /// </summary>
    Task<Guid?> GetOwnerRunIdAsync(string fileName, CancellationToken ct);
}
