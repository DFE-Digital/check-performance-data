using System.Security.Cryptography;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Media.Serving;
using DfE.CheckPerformanceData.Application.Media.Upload;
using Microsoft.Extensions.Logging;

namespace DfE.CheckPerformanceData.Application.Media.Embedded;

/// <summary>
/// Moves images embedded in page content as data: URIs into the media library.
///
/// Only current page versions change (see <see cref="IEmbeddedImageStore"/>). Pages are done one
/// at a time, so only one page's content is in memory, and each page is saved on its own: a run
/// that stops part way leaves finished pages finished, and the next run carries on.
///
/// Each page changes completely or not at all. Every image on the page is decoded first, so an
/// image that cannot be read stops the page before anything is stored. Then each different image
/// is uploaded through <see cref="IMediaUploadService"/>, one at a time, so the move waits its
/// turn at the processing gate like any upload. An image the library already holds is reused.
/// If the library refuses an image, the images this page added are deleted again and the page is
/// left as it was. Uploads cannot share the page's database transaction, because blob storage is
/// not transactional; the delete takes their place.
/// </summary>
public sealed class EmbeddedImageMover(
    IEmbeddedImageStore store,
    IMediaUploadService uploads,
    IMediaAssetRepository assets,
    IMediaDeletionService deletions,
    IMediaUrlBuilder urls,
    ICurrentUserService currentUser,
    TimeProvider time,
    ILogger<EmbeddedImageMover> logger) : IEmbeddedImageMover
{
    private const string FallbackName = "embedded-image-{0}";
    private const string StoreFailed = "The images could not be stored. Try again later.";

    public async Task<EmbeddedImageScan> ScanAsync(CancellationToken ct = default)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var pages = new List<EmbeddedImagePageScan>();
        var unique = new Dictionary<string, long>(StringComparer.Ordinal);
        var versionCount = 0;

        foreach (var page in await store.ListPagesAsync(now, ct))
        {
            var versions = await PageVersionsAsync(page, now, ct);
            if (versions.Count == 0) continue;

            var images = Distinct(versions);
            var refusals = new List<EmbeddedImageRefusal>();
            foreach (var image in images)
            {
                if (image.Decoded.Problem is { } problem)
                    refusals.Add(new EmbeddedImageRefusal(image.Name, problem));
                else
                    unique.TryAdd(Convert.ToHexStringLower(SHA256.HashData(image.Decoded.Bytes!)), image.Decoded.Bytes!.Length);
            }

            versionCount += versions.Count;
            pages.Add(new EmbeddedImagePageScan(page, versions.Select(v => v.Version.State).ToList(),
                versions.Sum(v => v.Images.Count), refusals));
        }

        return new EmbeddedImageScan(pages, versionCount, pages.Sum(p => p.ImageCount), unique.Count, unique.Values.Sum(),
            await store.CountOlderVersionsAsync(now, ct), await store.CountContentBlocksAsync(ct));
    }

    public async Task<EmbeddedImageMoveResult> MoveAsync(CancellationToken ct = default)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var runId = Guid.NewGuid();
        var userId = currentUser.UserId;
        var (changed, moved, created, reused) = (0, 0, 0, 0);
        var failures = new List<EmbeddedImagePageFailure>();

        foreach (var page in await store.ListPagesAsync(now, ct))
        {
            // Stop only between pages, so a page is never left half done.
            ct.ThrowIfCancellationRequested();
            var outcome = await MovePageAsync(page, now, userId);
            if (outcome.Failure is { } failure)
            {
                failures.Add(failure);
                continue;
            }
            if (outcome.ImagesMoved == 0) continue;

            changed++;
            moved += outcome.ImagesMoved;
            created += outcome.Created;
            reused += outcome.Reused;
        }

        var result = new EmbeddedImageMoveResult(changed, moved, created, reused, failures);
        await store.RecordRunAsync(runId, result, userId, CancellationToken.None);
        logger.LogInformation(
            "Embedded image move {RunId}: {PagesChanged} pages changed, {ImagesMoved} images moved, {Created} created, {Reused} reused, {Failed} pages failed",
            runId, changed, moved, created, reused, failures.Count);
        return result;
    }

    private sealed record PageOutcome(int ImagesMoved, int Created, int Reused, EmbeddedImagePageFailure? Failure);

    private async Task<PageOutcome> MovePageAsync(EmbeddedImagePage page, DateTime now, string? userId)
    {
        var ct = CancellationToken.None;
        var versions = await PageVersionsAsync(page, now, ct);
        if (versions.Count == 0) return new PageOutcome(0, 0, 0, null);

        var images = Distinct(versions);
        var problems = images
            .Where(i => i.Decoded.Problem is not null)
            .Select(i => new EmbeddedImageRefusal(i.Name, i.Decoded.Problem!))
            .ToList();
        if (problems.Count > 0) return Failed(page, problems);

        var createdIds = new List<Guid>();
        var library = new Dictionary<string, LibraryImageSrc>(StringComparer.Ordinal);
        var reused = 0;
        try
        {
            foreach (var image in images)
            {
                var upload = await uploads.UploadAsync(new MemoryStream(image.Decoded.Bytes!, writable: false), image.FileName, ct);
                if (upload.Outcome == MediaUploadOutcome.Rejected)
                {
                    await RemoveAsync(createdIds);
                    return Failed(page, [new EmbeddedImageRefusal(image.Name, upload.Rejection!)]);
                }

                var asset = upload.Asset!;
                if (upload.Outcome == MediaUploadOutcome.Created)
                {
                    createdIds.Add(asset.Id);
                    // The img's alt becomes the library's default. A reused image keeps its own.
                    if (!string.IsNullOrWhiteSpace(image.Alt))
                        await assets.UpdateMetadataAsync(asset.Id, MediaAssetMetadata.Empty with { DefaultAltText = image.Alt.Trim() }, ct);
                }
                else
                {
                    reused++;
                }

                library[image.Src] = new LibraryImageSrc(asset.Id, urls.Original(asset, asset.Latest));
            }

            var changes = versions
                .Select(v => new EmbeddedImageVersionChange(v.Version.Id, v.Version.Content,
                    EmbeddedImageContent.Replace(v.Version.Content, library)))
                .ToList();
            var imagesMoved = versions.Sum(v => v.Images.Count);
            var assetIds = library.Values.Select(l => l.Id).Distinct().ToList();

            if (!await store.SavePageAsync(new EmbeddedImagePageSave(page, changes, imagesMoved, assetIds), userId, ct))
            {
                await RemoveAsync(createdIds);
                return Failed(page, [new EmbeddedImageRefusal(page.Title, EmbeddedImageProblems.ChangedWhileMoving)]);
            }

            logger.LogInformation("Embedded image move: page {PageNodeId} changed, {ImagesMoved} images moved", page.NodeId, imagesMoved);
            return new PageOutcome(imagesMoved, createdIds.Count, reused, null);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Embedded image move: page {PageNodeId} left as it was", page.NodeId);
            await RemoveAsync(createdIds);
            return Failed(page, [new EmbeddedImageRefusal(page.Title, StoreFailed)]);
        }
    }

    // The images this page added, deleted again so a failed page leaves nothing behind. An image
    // that a page uses by now is kept: the deletion service refuses it.
    private async Task RemoveAsync(List<Guid> createdIds)
    {
        foreach (var id in createdIds)
        {
            try
            {
                await deletions.DeleteAsync(id, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Embedded image move: could not remove media item {AssetId} after a failed page", id);
            }
        }
    }

    private PageOutcome Failed(EmbeddedImagePage page, IReadOnlyList<EmbeddedImageRefusal> reasons)
    {
        logger.LogInformation("Embedded image move: page {PageNodeId} left as it was ({Count} problems)", page.NodeId, reasons.Count);
        return new PageOutcome(0, 0, 0, new EmbeddedImagePageFailure(page, reasons));
    }

    private sealed record VersionImages(EmbeddedImageVersion Version, IReadOnlyList<EmbeddedImage> Images);

    private sealed record PageImage(string Src, string? Alt, string Name, string FileName, DecodedDataImage Decoded);

    // The page's current versions that hold at least one embedded image.
    private async Task<List<VersionImages>> PageVersionsAsync(EmbeddedImagePage page, DateTime now, CancellationToken ct) =>
        (await store.GetCurrentVersionsAsync(page.NodeId, now, ct))
        .Select(v => new VersionImages(v, EmbeddedImageContent.Find(v.Content)))
        .Where(v => v.Images.Count > 0)
        .ToList();

    // Each different src on the page once, in document order, decoded. An image is named by its
    // alt text, then its title, then its position: "embedded-image-2".
    private static List<PageImage> Distinct(IEnumerable<VersionImages> versions)
    {
        var images = new List<PageImage>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var image in versions.SelectMany(v => v.Images))
        {
            if (!seen.Add(image.Src)) continue;

            var label = FirstText(image.Alt, image.Title);
            var name = label ?? string.Format(FallbackName, images.Count + 1);
            var decoded = DataImageUri.Decode(image.Src);
            var fileName = decoded.Format is { } format
                ? $"{name.Replace('/', ' ').Replace('\\', ' ')}.{MediaFormats.Extension(format)}"
                : name;
            images.Add(new PageImage(image.Src, image.Alt, name, fileName, decoded));
        }
        return images;
    }

    private static string? FirstText(params string?[] values) =>
        values.Select(v => v?.Trim()).FirstOrDefault(v => !string.IsNullOrEmpty(v));
}
