namespace DfE.CheckPerformanceData.Application.Media;

/// <summary>
/// The media library's records: images, their immutable file versions and where pages use them.
/// File bytes live in <see cref="IMediaBlobStore"/>; this seam holds only what describes them.
/// </summary>
public interface IMediaAssetRepository
{
    /// <summary>Adds an image and its version 1 in one save. Throws if the id is already taken.</summary>
    Task<MediaAssetDto> CreateAsync(NewMediaAsset asset, CancellationToken ct = default);

    /// <summary>
    /// Adds the next version and makes it current. <paramref name="version"/> must be exactly one
    /// above the current version (the caller stored the file under that number first); anything
    /// else, including losing a race to another replace, throws <see cref="MediaVersionConflictException"/>.
    /// A conflict also clears the scoped context's change tracker, so save other work before calling.
    /// Throws <see cref="KeyNotFoundException"/> when the image does not exist.
    /// </summary>
    Task<MediaAssetVersionDto> AddVersionAsync(Guid assetId, int version, NewMediaAssetVersion file, CancellationToken ct = default);

    Task<MediaAssetDto?> GetAsync(Guid assetId, CancellationToken ct = default);

    Task<MediaAssetVersionDto?> GetVersionAsync(Guid assetId, int version, CancellationToken ct = default);

    /// <summary>Every version of one image, newest first.</summary>
    Task<IReadOnlyList<MediaAssetVersionDto>> GetVersionsAsync(Guid assetId, CancellationToken ct = default);

    Task<MediaAssetPage> ListAsync(MediaAssetQuery query, CancellationToken ct = default);

    /// <summary>Replaces alt text, caption, credit and tags. Returns false when the image does not exist.</summary>
    Task<bool> UpdateMetadataAsync(Guid assetId, MediaAssetMetadata metadata, CancellationToken ct = default);

    /// <summary>Images with any version whose upload had this SHA-256, so an upload can offer the existing one.</summary>
    Task<IReadOnlyList<MediaAssetDto>> FindBySha256Async(string sha256, CancellationToken ct = default);
}
