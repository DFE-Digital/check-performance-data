namespace DfE.CheckPerformanceData.Application.Media.Embedded;

/// <summary>
/// The page content that the embedded-image move reads and changes. Only current page versions
/// (live, scheduled and the working draft) are listed and changed. Older versions keep their
/// embedded images, which still show because the sanitiser keeps a data: image on an img.
/// </summary>
public interface IEmbeddedImageStore
{
    /// <summary>Pages, not deleted, with a current version whose content holds a data: URI. By path.</summary>
    Task<IReadOnlyList<EmbeddedImagePage>> ListPagesAsync(DateTime nowUtc, CancellationToken ct = default);

    /// <summary>The page's current versions whose content holds a data: URI.</summary>
    Task<IReadOnlyList<EmbeddedImageVersion>> GetCurrentVersionsAsync(Guid nodeId, DateTime nowUtc, CancellationToken ct = default);

    /// <summary>Older page versions (not current) that hold a data: image. They are not changed.</summary>
    Task<int> CountOlderVersionsAsync(DateTime nowUtc, CancellationToken ct = default);

    /// <summary>Content blocks that hold a data: image. They are not changed.</summary>
    Task<int> CountContentBlocksAsync(CancellationToken ct = default);

    /// <summary>
    /// Saves the page's new content, its media usages and its audit entry in one save. Returns
    /// false, and changes nothing, when a version no longer holds the content it was read with.
    /// </summary>
    Task<bool> SavePageAsync(EmbeddedImagePageSave save, string? userId, CancellationToken ct = default);

    /// <summary>Writes the audit entry that sums up one run.</summary>
    Task RecordRunAsync(Guid runId, EmbeddedImageMoveResult result, string? userId, CancellationToken ct = default);
}
