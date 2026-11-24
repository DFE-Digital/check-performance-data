namespace DfE.CheckPerformanceData.Application.Media.Embedded;

/// <summary>
/// The page and content block content that the embedded-image move reads and changes. Only
/// current page versions (live, scheduled and the working draft) are listed. Each one that changes
/// gets a new version that takes its place; the old version stays in the history as it was. Older
/// versions keep their embedded images, which still show because the sanitiser keeps a data: image
/// on an img. A content block's current value changes.
/// </summary>
public interface IEmbeddedImageStore
{
    /// <summary>Pages, not deleted, with a current version whose content holds a data: URI. By path.</summary>
    Task<IReadOnlyList<EmbeddedImagePage>> ListPagesAsync(DateTime nowUtc, CancellationToken ct = default);

    /// <summary>The page's current versions whose content holds a data: URI.</summary>
    Task<IReadOnlyList<EmbeddedImageVersion>> GetCurrentVersionsAsync(Guid nodeId, DateTime nowUtc, CancellationToken ct = default);

    /// <summary>Older page versions (not current) that hold a data: image. They are not changed.</summary>
    Task<int> CountOlderVersionsAsync(DateTime nowUtc, CancellationToken ct = default);

    /// <summary>Content blocks of type Content whose value holds a data: URI. By key.</summary>
    Task<IReadOnlyList<EmbeddedImageBlock>> ListContentBlocksAsync(CancellationToken ct = default);

    /// <summary>
    /// Adds a new version for each changed version, with its media usages and the page's audit
    /// entry, in one save. A live version gets a new live version and a scheduled version a new
    /// scheduled version, both on the same window; a draft gets a new draft. Returns false, and
    /// changes nothing, when a version is no longer current or no longer holds the content it was
    /// read with.
    /// </summary>
    Task<bool> SavePageAsync(EmbeddedImagePageSave save, string? userId, CancellationToken ct = default);

    /// <summary>
    /// Saves the block's new value, a new version in its history, its media usages and its audit
    /// entry in one save. Returns false, and changes nothing, when the block no longer holds the
    /// value it was read with.
    /// </summary>
    Task<bool> SaveContentBlockAsync(EmbeddedImageBlockSave save, string? userId, CancellationToken ct = default);

    /// <summary>Writes the audit entry that sums up one run.</summary>
    Task RecordRunAsync(Guid runId, EmbeddedImageMoveResult result, string? userId, CancellationToken ct = default);
}
