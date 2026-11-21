using System.Security.Cryptography;
using DfE.CheckPerformanceData.Application.Media;
using DfE.CheckPerformanceData.Application.Media.Upload;
using Microsoft.Extensions.Logging;

namespace DfE.CheckPerformanceData.Application.ContentStaging;

/// <summary>
/// Moves the media library images that staged content uses between environments.
///
/// Export: every image a page version or Content block refers to, with its current version and
/// every older version an exported page version is pinned to. Each file goes in the bundle once,
/// keyed by the SHA-256 of its bytes.
///
/// Import keeps each image's id, because content stores the id. For each bundle version:
/// <list type="number">
/// <item>The bytes must match the SHA-256 the bundle gives them, or the file is refused.</item>
/// <item>A target version of the same image with that hash is reused.</item>
/// <item>Otherwise the bytes go through the upload checks (type sniffing, limits, re-encoding,
/// SVG sanitiser). A target version with the hash of the cleaned bytes is reused.</item>
/// <item>Otherwise the cleaned bytes are stored as the image's next version, under the hash of
/// the stored bytes. Re-encoding can change a JPEG's bytes, so this hash can differ from the
/// bundle's. That is expected, and the same bundle imported again finds the cleaned copy.</item>
/// </list>
/// The bundle's current version is reused only when it matches the target's current version, so
/// the image always ends on the bundle's current file. Older versions match any target version.
/// Type, size and the animated flag come from the cleaned file, never from the bundle. An image
/// that already exists keeps its own alt text, caption, credit and tags.
/// </summary>
public sealed class ContentMediaStaging(
    IMediaAssetRepository assets,
    IMediaBlobStore blobs,
    IMediaFileCleaner cleaner,
    MediaProcessingGate gate,
    ILogger<ContentMediaStaging> logger) : IContentMediaStaging
{
    // The column sizes of media_assets, and the tag limits of the library's edit form.
    private const int MaxAltText = 1000;
    private const int MaxCaption = 2000;
    private const int MaxCredit = 500;
    private const int MaxTags = 20;
    private const int MaxTagLength = 100;

    public async Task<IReadOnlyList<MediaPinBundleItem>> PinsForAsync(
        Guid pageVersionId, string content, CancellationToken ct = default)
    {
        var ids = MediaReferenceExtractor.Extract(content).Select(r => r.AssetId).Distinct().ToList();
        if (ids.Count == 0) return [];

        var pinned = await assets.GetPinnedVersionsAsync(pageVersionId, ids, ct);
        return pinned
            .GroupBy(v => v.AssetId)
            .Select(g => new MediaPinBundleItem { MediaId = g.Key, Version = g.Max(v => v.Version) })
            .OrderBy(p => p.MediaId)
            .ToList();
    }

    public async Task<ContentBundleMedia> CollectAsync(
        IReadOnlyCollection<PageNodeBundleItem> pages, IReadOnlyCollection<ContentBlockBundleItem> blocks,
        CancellationToken ct = default)
    {
        var wanted = new Dictionary<Guid, SortedSet<int>>();
        foreach (var version in pages.SelectMany(p => p.Versions))
        {
            foreach (var reference in MediaReferenceExtractor.Extract(version.Content))
                VersionsOf(wanted, reference.AssetId);
            foreach (var pin in version.Media)
                VersionsOf(wanted, pin.MediaId).Add(pin.Version);
        }
        foreach (var reference in blocks.Where(b => b.BlockType == "Content")
                     .SelectMany(b => MediaReferenceExtractor.ExtractRichText(b.Value)))
            VersionsOf(wanted, reference.AssetId);

        if (wanted.Count == 0) return ContentBundleMedia.Empty;

        var media = ContentBundleMedia.Empty;
        var fileHashes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var asset in (await assets.GetManyAsync(wanted.Keys, ct)).OrderBy(a => a.Id))
        {
            var numbers = wanted[asset.Id].Where(n => n >= 1 && n <= asset.CurrentVersion).Append(asset.CurrentVersion).Distinct().Order();
            var versions = new List<MediaAssetVersionBundleItem>();
            foreach (var number in numbers)
            {
                var stored = number == asset.CurrentVersion ? asset.Latest : await assets.GetVersionAsync(asset.Id, number, ct);
                var bytes = stored is null ? null : await ReadAsync(stored.BlobKey, ct);
                if (stored is null || bytes is null)
                {
                    logger.LogWarning("Content export: media {AssetId} version {Version} has no stored file; left out", asset.Id, number);
                    continue;
                }

                var sha256 = Sha256(bytes);
                if (fileHashes.Add(sha256)) media.Files.Add(new MediaFileBundleItem { Sha256 = sha256, Data = bytes });
                versions.Add(new MediaAssetVersionBundleItem
                {
                    Version = number,
                    Sha256 = sha256,
                    ContentType = stored.ContentType,
                    Width = stored.Width,
                    Height = stored.Height,
                    SizeBytes = bytes.Length,
                    IsAnimated = stored.IsAnimated,
                });
            }

            if (versions.Count == 0) continue;
            media.Assets.Add(new MediaAssetBundleItem
            {
                Id = asset.Id,
                FileName = asset.FileName,
                DefaultAltText = asset.DefaultAltText,
                Caption = asset.Caption,
                Credit = asset.Credit,
                Tags = [.. asset.Tags],
                CurrentVersion = asset.CurrentVersion,
                Versions = versions,
            });
        }

        return media;
    }

    public async Task<MediaImportMap> ImportAsync(ContentBundle bundle, ContentImportResult result, CancellationToken ct = default)
    {
        var map = new MediaImportMap();
        var files = bundle.MediaFiles
            .GroupBy(f => f.Sha256.ToLowerInvariant(), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        foreach (var asset in bundle.MediaAssets.GroupBy(a => a.Id).Select(g => g.First()))
        {
            if (asset.Id == Guid.Empty)
            {
                result.Warnings.Add($"Did not import image ‘{MediaFileNames.Safe(asset.FileName)}’: it has no id.");
                continue;
            }

            try
            {
                await ImportAssetAsync(asset, files, map, result, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Content import: media {AssetId} failed to import", asset.Id);
                result.Warnings.Add($"Could not import image {Describe(asset)}: {ex.GetType().Name}.");
            }
        }

        await WarnAboutMissingImagesAsync(bundle, result, ct);
        return map;
    }

    private async Task ImportAssetAsync(
        MediaAssetBundleItem asset, Dictionary<string, MediaFileBundleItem> files, MediaImportMap map,
        ContentImportResult result, CancellationToken ct)
    {
        var existing = await assets.GetAsync(asset.Id, ct);
        var targetVersions = existing is null ? [] : (await assets.GetVersionsAsync(asset.Id, ct)).ToList();
        var current = existing?.CurrentVersion ?? 0;
        if (existing is null)
        {
            // No row owns files under this id, so any found are left from an image deleted earlier.
            // Old renditions would be served for the new file, so they go first.
            foreach (var prefix in MediaBlobKeys.AssetPrefixes(asset.Id))
                await blobs.DeleteByPrefixAsync(prefix, ct);
        }

        var versions = asset.Versions.GroupBy(v => v.Version).Select(g => g.First()).OrderBy(v => v.Version).ToList();
        if (versions.Count == 0) return;
        var bundleCurrent = versions.Any(v => v.Version == asset.CurrentVersion) ? asset.CurrentVersion : versions[^1].Version;

        var added = 0;
        var mapped = 0;
        foreach (var version in versions)
        {
            var label = $"{Describe(asset)} version {version.Version}";
            if (!files.TryGetValue(version.Sha256.ToLowerInvariant(), out var file))
            {
                result.Warnings.Add($"Did not import image {label}: the bundle has no file for it.");
                continue;
            }

            var bundleHash = Sha256(file.Data);
            if (!string.Equals(bundleHash, version.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                result.Warnings.Add($"Did not import image {label}: its file does not match its SHA-256.");
                continue;
            }

            // The bundle's current file must end up current, so it may match only the current version.
            var isCurrent = version.Version == bundleCurrent;
            MediaAssetVersionDto? Match(string sha256) => targetVersions.FirstOrDefault(t =>
                t.Sha256 == sha256 && (!isCurrent || t.Version == current));

            if (Match(bundleHash) is { } same)
            {
                map.Add(asset.Id, version.Version, same.Version);
                mapped++;
                continue;
            }

            MediaCheckResult check;
            using (await gate.EnterAsync(ct))
                check = await cleaner.CleanAsync(new MemoryStream(file.Data, writable: false), ct);
            if (!check.IsAccepted)
            {
                result.Warnings.Add($"Did not import image {label}: {check.Rejection}");
                continue;
            }

            var cleaned = check.File!;
            var cleanedHash = Sha256(cleaned.Bytes);
            if (Match(cleanedHash) is { } cleanedSame)
            {
                map.Add(asset.Id, version.Version, cleanedSame.Version);
                mapped++;
                continue;
            }

            var stored = await StoreAsync(asset, current + 1, cleaned, cleanedHash, label, result, ct);
            if (stored is null) continue;

            targetVersions.Add(stored);
            current = stored.Version;
            map.Add(asset.Id, version.Version, stored.Version);
            added++;
        }

        if (existing is null)
        {
            if (added > 0) result.MediaAssetsCreated++;
        }
        else if (added > 0)
        {
            result.MediaAssetsUpdated++;
        }
        else if (mapped > 0)
        {
            result.MediaAssetsReused++;
        }
    }

    // Stores the file, then its row: a row must never point at a missing file. A failed row save
    // removes the file again.
    private async Task<MediaAssetVersionDto?> StoreAsync(
        MediaAssetBundleItem asset, int number, CleanedMediaFile file, string sha256, string label,
        ContentImportResult result, CancellationToken ct)
    {
        var key = MediaBlobKeys.Original(asset.Id, number);
        if (!await blobs.PutAsync(key, new MemoryStream(file.Bytes, writable: false), file.ContentType, ct))
        {
            logger.LogWarning("Content import: media blob {BlobKey} already exists; version not imported", key);
            result.Warnings.Add($"Did not import image {label}: the library changed this image at the same time. Import again.");
            return null;
        }

        var row = new NewMediaAssetVersion(key, file.ContentType, file.Width, file.Height, file.Bytes.Length, sha256, file.IsAnimated);
        try
        {
            if (number == 1)
            {
                var name = MediaFileNames.Safe(asset.FileName);
                var created = await assets.CreateAsync(new NewMediaAsset(asset.Id, name, MediaFileNames.Slug(name), Metadata(asset), row), ct);
                return created.Latest;
            }
            return await assets.AddVersionAsync(asset.Id, number, row, ct);
        }
        catch (MediaVersionConflictException)
        {
            await DeleteQuietlyAsync(key);
            result.Warnings.Add($"Did not import image {label}: the library changed this image at the same time. Import again.");
            return null;
        }
        catch
        {
            await DeleteQuietlyAsync(key);
            throw;
        }
    }

    // One warning for each image that content in the bundle uses but that is in neither the bundle
    // nor this library. The content still imports; the image does not show.
    private async Task WarnAboutMissingImagesAsync(ContentBundle bundle, ContentImportResult result, CancellationToken ct)
    {
        var firstUser = new Dictionary<Guid, string>();
        foreach (var page in bundle.PageNodes)
            foreach (var reference in page.Versions.SelectMany(v => MediaReferenceExtractor.Extract(v.Content)))
                firstUser.TryAdd(reference.AssetId, $"page ‘{page.Title}’");
        foreach (var block in bundle.ContentBlocks.Where(b => b.BlockType == "Content"))
            foreach (var reference in MediaReferenceExtractor.ExtractRichText(block.Value))
                firstUser.TryAdd(reference.AssetId, $"content block ‘{block.Key}’");

        if (firstUser.Count == 0) return;

        var present = (await assets.GetManyAsync(firstUser.Keys, ct)).Select(a => a.Id).ToHashSet();
        foreach (var (id, user) in firstUser.Where(u => !present.Contains(u.Key)))
            result.Warnings.Add($"The {user} uses image {id}, which is in neither the bundle nor this library, so it does not show.");
    }

    private static MediaAssetMetadata Metadata(MediaAssetBundleItem asset) => new(
        Cut(asset.DefaultAltText, MaxAltText),
        Cut(asset.Caption, MaxCaption),
        Cut(asset.Credit, MaxCredit),
        asset.Tags
            .Select(t => Cut(t, MaxTagLength))
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .Take(MaxTags)
            .ToList());

    private static string? Cut(string? value, int max)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }

    private static string Describe(MediaAssetBundleItem asset) =>
        $"‘{MediaFileNames.Safe(asset.FileName)}’ ({asset.Id})";

    private static SortedSet<int> VersionsOf(Dictionary<Guid, SortedSet<int>> wanted, Guid id)
    {
        if (!wanted.TryGetValue(id, out var versions)) wanted[id] = versions = [];
        return versions;
    }

    private async Task<byte[]?> ReadAsync(string key, CancellationToken ct)
    {
        await using var blob = await blobs.GetAsync(key, ct);
        if (blob is null) return null;
        using var buffer = new MemoryStream();
        await blob.Content.CopyToAsync(buffer, ct);
        return buffer.ToArray();
    }

    private static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    // Runs after a failure, so it must not hide that failure.
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
}
