namespace DfE.CheckPerformanceData.Application.Media.Upload;

/// <summary>
/// Adds files to the media library. Every file is checked and cleaned first (see
/// <see cref="IMediaFileCleaner"/>), then stored in blob storage, then recorded. The signed-in user
/// is recorded as the uploader, and the generic audit capture logs the new rows.
/// </summary>
public interface IMediaUploadService
{
    /// <summary>
    /// Adds a new image. When the library already holds the same cleaned file, nothing is stored
    /// and the result names the existing image so the editor can use it instead.
    /// </summary>
    Task<MediaUploadResult> UploadAsync(Stream content, string fileName, CancellationToken ct = default);

    /// <summary>
    /// Stores a new version of an existing image and makes it current. Earlier versions stay, so
    /// old page versions keep showing the file they used.
    /// </summary>
    Task<MediaReplaceResult> ReplaceAsync(Guid assetId, Stream content, CancellationToken ct = default);
}
