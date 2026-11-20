using System.Security.Cryptography;
using DfE.CheckPerformanceData.Application.Wiki;
using Microsoft.Extensions.Logging;

namespace DfE.CheckPerformanceData.Application.Media.Upload;

/// <summary>
/// Adds and replaces media library files.
///
/// Order: clean the file, look for a duplicate, store the bytes, then save the row. The row comes
/// second because the blob key is chosen first and a row must never point at a missing file. When
/// the row fails to save, the file this call stored is deleted again. Only a crash between the two
/// steps leaves a stored file with no row; nothing reads such a file. A later replace that needs
/// the same version key is refused rather than overwrite it, and the warning log names the key so
/// it can be removed.
///
/// Duplicates are found by the SHA-256 of the cleaned bytes, the bytes actually stored. So two
/// uploads that differ only in metadata (for example the same photo with and without GPS data)
/// count as one image, and the stored hash always matches the stored file. Only an image whose
/// current version has the hash is a duplicate.
/// </summary>
public sealed class MediaUploadService(
    IMediaFileCleaner cleaner,
    IMediaAssetRepository assets,
    IMediaBlobStore blobs,
    ILogger<MediaUploadService> logger) : IMediaUploadService
{
    private const int MaxFileNameLength = 255;
    private const int MaxSlugLength = 200;
    private const string FallbackName = "image";

    public async Task<MediaUploadResult> UploadAsync(Stream content, string fileName, CancellationToken ct = default)
    {
        var check = await cleaner.CleanAsync(content, ct);
        if (!check.IsAccepted)
        {
            logger.LogInformation("Media upload refused: {Reason}", check.Rejection);
            return MediaUploadResult.Rejected(check.Rejection!);
        }

        var file = check.File!;
        var sha256 = Sha256(file.Bytes);
        // Only an image that shows this file now is the same image; one that held it in an older
        // version has been replaced since.
        var existing = (await assets.FindBySha256Async(sha256, ct)).FirstOrDefault(a => a.Latest.Sha256 == sha256);
        if (existing is not null) return MediaUploadResult.Duplicate(existing);

        var id = Guid.NewGuid();
        var key = MediaBlobKeys.Original(id, 1);
        if (!await blobs.PutAsync(key, new MemoryStream(file.Bytes, writable: false), file.ContentType, ct))
            throw new InvalidOperationException($"The new media file key {key} is already taken.");

        var name = SafeFileName(fileName);
        try
        {
            var created = await assets.CreateAsync(
                new NewMediaAsset(id, name, Slug(name), MediaAssetMetadata.Empty, Version(key, file, sha256)), ct);
            logger.LogInformation("Media item {AssetId} uploaded ({ContentType}, {SizeBytes} bytes)", id, file.ContentType, file.Bytes.Length);
            return MediaUploadResult.Created(created);
        }
        catch
        {
            await DeleteQuietlyAsync(key);
            throw;
        }
    }

    public async Task<MediaReplaceResult> ReplaceAsync(Guid assetId, Stream content, CancellationToken ct = default)
    {
        var asset = await assets.GetAsync(assetId, ct);
        if (asset is null) return MediaReplaceResult.NotFound();

        var check = await cleaner.CleanAsync(content, ct);
        if (!check.IsAccepted)
        {
            logger.LogInformation("Media replace for {AssetId} refused: {Reason}", assetId, check.Rejection);
            return MediaReplaceResult.Rejected(check.Rejection!);
        }

        var file = check.File!;
        var version = asset.CurrentVersion + 1;
        var key = MediaBlobKeys.Original(assetId, version);
        if (!await blobs.PutAsync(key, new MemoryStream(file.Bytes, writable: false), file.ContentType, ct))
        {
            // Another replace is storing this version now, or an earlier one stopped between
            // storing its file and saving its row. Either way the file is not ours to touch.
            logger.LogWarning("Media replace for {AssetId} refused: blob {BlobKey} already exists", assetId, key);
            return MediaReplaceResult.Rejected(MediaRejections.ReplacedConcurrently);
        }

        try
        {
            var added = await assets.AddVersionAsync(assetId, version, Version(key, file, Sha256(file.Bytes)), ct);
            logger.LogInformation("Media item {AssetId} replaced with version {Version}", assetId, version);
            return MediaReplaceResult.Replaced(added);
        }
        catch (MediaVersionConflictException)
        {
            await DeleteQuietlyAsync(key);
            return MediaReplaceResult.Rejected(MediaRejections.ReplacedConcurrently);
        }
        catch (KeyNotFoundException)
        {
            await DeleteQuietlyAsync(key);
            return MediaReplaceResult.NotFound();
        }
        catch
        {
            await DeleteQuietlyAsync(key);
            throw;
        }
    }

    private static NewMediaAssetVersion Version(string key, CleanedMediaFile file, string sha256) =>
        new(key, file.ContentType, file.Width, file.Height, file.Bytes.Length, sha256, file.IsAnimated);

    private static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    // Runs after a failure, so it must not hide that failure: it never throws and is not cancelled.
    private async Task DeleteQuietlyAsync(string key)
    {
        try
        {
            await blobs.DeleteAsync(key, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not delete media blob {BlobKey} after a failed save; it has no row", key);
        }
    }

    // The last part of the name the browser sent, with any folder path removed, cut to fit.
    private static string SafeFileName(string fileName)
    {
        var name = Path.GetFileName((fileName ?? "").Replace('\\', '/').Trim());
        if (name.Length == 0) return FallbackName;
        if (name.Length <= MaxFileNameLength) return name;

        var extension = Path.GetExtension(name);
        if (extension.Length >= MaxFileNameLength / 2) extension = "";
        return name[..(MaxFileNameLength - extension.Length)] + extension;
    }

    private static string Slug(string fileName)
    {
        var slug = WikiSlug.Generate(Path.GetFileNameWithoutExtension(fileName));
        if (slug.Length > MaxSlugLength) slug = slug[..MaxSlugLength].TrimEnd('-');
        return slug.Length == 0 ? FallbackName : slug;
    }
}
