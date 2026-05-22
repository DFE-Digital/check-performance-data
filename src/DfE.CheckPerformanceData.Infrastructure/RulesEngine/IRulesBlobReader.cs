namespace DfE.CheckPerformanceData.Infrastructure.RulesEngine;

/// <summary>
/// Thin abstraction over the bit of Azure Blob storage the provider actually
/// uses. Lets <see cref="BlobRulesProvider"/> be unit-tested without mocking
/// Azure SDK types: tests substitute a fake that returns canned
/// <see cref="RulesBlobFetch"/> values.
/// </summary>
public interface IRulesBlobReader
{
    /// <summary>
    /// Fetch <paramref name="blobName"/>. If <paramref name="ifNoneMatch"/> is
    /// supplied and the blob's current ETag matches, the returned fetch is
    /// <see cref="RulesBlobFetch.NotModified"/> with no body. On any error the
    /// returned fetch is <see cref="RulesBlobFetch.Error"/> with a reason.
    /// </summary>
    Task<RulesBlobFetch> FetchAsync(string blobName, string? ifNoneMatch, CancellationToken ct);
}

/// <summary>
/// Result of a single fetch. The two non-success states are explicit so the
/// provider can branch on them without exception handling on the hot path.
/// </summary>
public sealed record RulesBlobFetch
{
    public bool IsSuccess { get; init; }
    public bool IsNotModified { get; init; }
    public bool IsError { get; init; }
    public string? ETag { get; init; }
    public string? Content { get; init; }
    public string? ErrorReason { get; init; }

    public static RulesBlobFetch Success(string content, string? eTag) =>
        new() { IsSuccess = true, Content = content, ETag = eTag };

    public static RulesBlobFetch NotModified(string? eTag) =>
        new() { IsNotModified = true, ETag = eTag };

    public static RulesBlobFetch Error(string reason) =>
        new() { IsError = true, ErrorReason = reason };
}
