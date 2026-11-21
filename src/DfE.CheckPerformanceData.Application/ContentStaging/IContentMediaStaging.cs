namespace DfE.CheckPerformanceData.Application.ContentStaging;

/// <summary>
/// Carries the media library images that staged content uses: Image widgets (mediaId) and
/// rich-text images (data-media-id or a /media/{id}/ address) in page versions and Content
/// blocks.
/// </summary>
public interface IContentMediaStaging
{
    /// <summary>The image versions recorded for one page version (its row id) when it was saved.</summary>
    Task<IReadOnlyList<MediaPinBundleItem>> PinsForAsync(Guid pageVersionId, string content, CancellationToken ct = default);

    /// <summary>
    /// The images the exported pages and blocks use, with the current version of each and every
    /// pinned version, and each file once (by SHA-256). An image that is not in the library is left out.
    /// </summary>
    Task<ContentBundleMedia> CollectAsync(
        IReadOnlyCollection<PageNodeBundleItem> pages, IReadOnlyCollection<ContentBlockBundleItem> blocks,
        CancellationToken ct = default);

    /// <summary>
    /// Brings the bundle's images into the library before the content that uses them, keeping
    /// their ids. A version whose file the library already holds is reused. Every problem is a
    /// warning on <paramref name="result"/>; nothing here stops the import.
    /// </summary>
    Task<MediaImportMap> ImportAsync(ContentBundle bundle, ContentImportResult result, CancellationToken ct = default);
}
