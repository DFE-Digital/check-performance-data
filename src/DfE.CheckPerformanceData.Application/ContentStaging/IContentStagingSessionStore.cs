namespace DfE.CheckPerformanceData.Application.ContentStaging;

/// <summary>
/// Holds a parsed import bundle server-side for the gap between the Preview step and the
/// Import step.
///
/// The two steps used to be joined by round-tripping the whole bundle through a hidden form
/// field: Preview parsed the upload, re-serialised it into the HTML it returned, the browser
/// posted it all back, and Import parsed it a second time. That put the entire bundle on the
/// wire twice and through the model binder once, which is what forced the request-form limits
/// up to 64 MB — a 5.8 MB bundle was rejected with an empty-body 400 by the default 4 MB
/// per-form-value ceiling before the action ever ran.
///
/// Keeping the bundle here instead means the only thing that travels between the two steps is
/// the session id.
/// </summary>
public interface IContentStagingSessionStore
{
    /// <summary>Persists a parsed bundle and returns the id the Import step will quote back.</summary>
    Task<Guid> CreateAsync(string bundleJson, string? createdBy, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the stored bundle, or null when the id is unknown or the session has expired.
    /// An expired row reads as absent whether or not the purge has caught up with it, so a
    /// late Import can never resurrect a bundle past its lifetime.
    /// </summary>
    Task<string?> GetBundleJsonAsync(Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>Drops a session once its import has succeeded.</summary>
    Task DeleteAsync(Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>Deletes every session past its expiry. Returns how many rows went.</summary>
    Task<int> PurgeExpiredAsync(CancellationToken cancellationToken = default);
}

public static class ContentStagingSessionDefaults
{
    /// <summary>
    /// How long a previewed bundle stays resumable. Long enough that an administrator can
    /// preview a large import, go and check something with a colleague, and come back to
    /// confirm it without re-uploading; short enough that an abandoned preview of a big
    /// bundle does not sit in the database indefinitely.
    /// </summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);
}
