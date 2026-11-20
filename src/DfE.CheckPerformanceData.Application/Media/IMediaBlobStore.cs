namespace DfE.CheckPerformanceData.Application.Media;

/// <summary>
/// The bytes of media files: uploaded originals and the resized renditions made from them, in the
/// cms-media container. Keys come from <see cref="MediaBlobKeys"/>. Stored files are never
/// overwritten: an original is immutable, and a rendition is a pure function of its key, so a
/// second write of either is a no-op that reports false.
/// </summary>
public interface IMediaBlobStore
{
    /// <summary>Stores the file. Returns false, leaving the existing file untouched, when the key is already taken.</summary>
    Task<bool> PutAsync(string key, Stream content, string contentType, CancellationToken ct = default);

    /// <summary>Opens the file, or returns null when there is none.</summary>
    Task<MediaBlob?> GetAsync(string key, CancellationToken ct = default);

    Task<bool> ExistsAsync(string key, CancellationToken ct = default);

    /// <summary>Removes the file. Returns false when there was nothing to remove.</summary>
    Task<bool> DeleteAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Removes every file whose key starts with <paramref name="prefix"/> and returns how many it
    /// removed. Refuses an empty prefix, which would match the whole container.
    /// </summary>
    Task<int> DeleteByPrefixAsync(string prefix, CancellationToken ct = default);
}
