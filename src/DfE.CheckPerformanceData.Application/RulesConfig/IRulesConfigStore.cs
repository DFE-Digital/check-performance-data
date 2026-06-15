namespace DfE.CheckPerformanceData.Application.RulesConfig;

/// <summary>
/// Read/write access to the two rules-config blobs (rules.json, country-languages.json).
/// Reads return content + ETag; writes pass the expected ETag so a concurrent change is
/// rejected with <see cref="RulesConfigConflictException"/> rather than silently clobbered.
/// Pass <c>expectedETag = null</c> only to create a blob that does not yet exist.
/// </summary>
public interface IRulesConfigStore
{
    Task<RulesConfigBlob> ReadAsync(RulesConfigType type, CancellationToken ct = default);
    Task WriteAsync(RulesConfigType type, string content, string? expectedETag, CancellationToken ct = default);
}
