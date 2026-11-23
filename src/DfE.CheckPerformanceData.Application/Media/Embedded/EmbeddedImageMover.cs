using System.Security.Cryptography;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Media.Serving;
using DfE.CheckPerformanceData.Application.Media.Upload;
using Microsoft.Extensions.Logging;

namespace DfE.CheckPerformanceData.Application.Media.Embedded;

/// <summary>
/// Moves images embedded in page content and content blocks as data: URIs into the media library.
///
/// Only current page versions get new versions, and only the blocks' current values change (see
/// <see cref="IEmbeddedImageStore"/>). Pages, then blocks, are done one at a time, and each is
/// saved on its own: a run that stops part way leaves finished items finished, and the next run
/// carries on. Only one page's content is in memory. Blocks are short, so the blocks that hold a
/// data: URI are listed with their values.
///
/// Each page or block changes completely or not at all. Every image in it is decoded first, so an
/// image that cannot be read stops it before anything is stored. Then each different image
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

            var refusals = Check(Distinct(versions.SelectMany(v => v.Images)), unique);
            versionCount += versions.Count;
            pages.Add(new EmbeddedImagePageScan(page, versions.Select(v => v.Version.State).ToList(),
                versions.Sum(v => v.Images.Count), refusals));
        }

        var blocks = new List<EmbeddedImageBlockScan>();
        foreach (var block in await store.ListContentBlocksAsync(ct))
        {
            var images = EmbeddedImageContent.FindInRichText(block.Content);
            if (images.Count == 0) continue;
            blocks.Add(new EmbeddedImageBlockScan(block, images.Count, Check(Distinct(images), unique)));
        }

        return new EmbeddedImageScan(pages, versionCount,
            pages.Sum(p => p.ImageCount) + blocks.Sum(b => b.ImageCount), unique.Count, unique.Values.Sum(),
            await store.CountOlderVersionsAsync(now, ct), blocks);
    }

    // The images that cannot move. Each readable image is counted once in unique, by its hash.
    private static List<EmbeddedImageRefusal> Check(List<PageImage> images, Dictionary<string, long> unique)
    {
        var refusals = new List<EmbeddedImageRefusal>();
        foreach (var image in images)
        {
            if (image.Decoded.Problem is { } problem)
                refusals.Add(new EmbeddedImageRefusal(image.Name, problem));
            else
                unique.TryAdd(Convert.ToHexStringLower(SHA256.HashData(image.Decoded.Bytes!)), image.Decoded.Bytes!.Length);
        }
        return refusals;
    }

    public async Task<EmbeddedImageMoveResult> MoveAsync(string? modifiedBy, CancellationToken ct = default)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var runId = Guid.NewGuid();
        var userId = currentUser.UserId;
        var (changed, moved, created, reused) = (0, 0, 0, 0);
        var failures = new List<EmbeddedImagePageFailure>();

        foreach (var page in await store.ListPagesAsync(now, ct))
        {
            // Stop only between pages, so a page is never left half done. A stopped run still
            // records what it did, and the next run carries on.
            if (ct.IsCancellationRequested) break;
            var outcome = await MovePageAsync(page, now, userId, modifiedBy);
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

        var blocksChanged = 0;
        var blockFailures = new List<EmbeddedImageBlockFailure>();
        if (!ct.IsCancellationRequested)
        {
            foreach (var block in await store.ListContentBlocksAsync(ct))
            {
                if (ct.IsCancellationRequested) break;
                var outcome = await MoveBlockAsync(block, userId);
                if (outcome.Refusals is { } reasons)
                {
                    blockFailures.Add(new EmbeddedImageBlockFailure(block, reasons));
                    continue;
                }
                if (outcome.ImagesMoved == 0) continue;

                blocksChanged++;
                moved += outcome.ImagesMoved;
                created += outcome.Created;
                reused += outcome.Reused;
            }
        }

        var result = new EmbeddedImageMoveResult(changed, moved, created, reused, failures, blocksChanged, blockFailures);
        await store.RecordRunAsync(runId, result, userId, CancellationToken.None);
        logger.LogInformation(
            "Embedded image move {RunId}: {PagesChanged} pages and {BlocksChanged} content blocks changed, {ImagesMoved} images moved, {Created} created, {Reused} reused, {Failed} pages and {BlocksFailed} content blocks failed",
            runId, changed, blocksChanged, moved, created, reused, failures.Count, blockFailures.Count);
        return result;
    }

    private sealed record PageOutcome(int ImagesMoved, int Created, int Reused, EmbeddedImagePageFailure? Failure);

    // What moving one page or block did. Refusals is set when it was left as it was.
    private sealed record MoveOutcome(int ImagesMoved, int Created, int Reused, IReadOnlyList<EmbeddedImageRefusal>? Refusals);

    private async Task<PageOutcome> MovePageAsync(EmbeddedImagePage page, DateTime now, string? userId, string? modifiedBy)
    {
        var versions = await PageVersionsAsync(page, now, CancellationToken.None);
        if (versions.Count == 0) return new PageOutcome(0, 0, 0, null);

        var imagesMoved = versions.Sum(v => v.Images.Count);
        var outcome = await MoveImagesAsync(Distinct(versions.SelectMany(v => v.Images)), page.Title, "page", page.NodeId,
            (library, assetIds) => store.SavePageAsync(new EmbeddedImagePageSave(page,
                versions.Select(v => new EmbeddedImageVersionChange(v.Version.Id, v.Version.Content,
                    EmbeddedImageContent.Replace(v.Version.Content, library))).ToList(),
                imagesMoved, assetIds, modifiedBy), userId, CancellationToken.None),
            imagesMoved);

        return outcome.Refusals is { } reasons
            ? new PageOutcome(0, 0, 0, new EmbeddedImagePageFailure(page, reasons))
            : new PageOutcome(outcome.ImagesMoved, outcome.Created, outcome.Reused, null);
    }

    private async Task<MoveOutcome> MoveBlockAsync(EmbeddedImageBlock block, string? userId)
    {
        var images = EmbeddedImageContent.FindInRichText(block.Content);
        if (images.Count == 0) return new MoveOutcome(0, 0, 0, null);

        return await MoveImagesAsync(Distinct(images), block.Key, "content block", block.Id,
            (library, assetIds) => store.SaveContentBlockAsync(new EmbeddedImageBlockSave(block,
                EmbeddedImageContent.ReplaceInRichText(block.Content, library), images.Count, assetIds), userId, CancellationToken.None),
            images.Count);
    }

    // Uploads each different image, then saves the page or block through save, which gets the
    // library image for each src. Any problem leaves it as it was and deletes the images it added.
    private async Task<MoveOutcome> MoveImagesAsync(
        List<PageImage> images, string title, string kind, object id,
        Func<IReadOnlyDictionary<string, LibraryImageSrc>, IReadOnlyList<Guid>, Task<bool>> save, int imagesMoved)
    {
        var ct = CancellationToken.None;
        var problems = images
            .Where(i => i.Decoded.Problem is not null)
            .Select(i => new EmbeddedImageRefusal(i.Name, i.Decoded.Problem!))
            .ToList();
        if (problems.Count > 0) return Failed(kind, id, problems);

        var createdIds = new List<Guid>();
        var library = new Dictionary<string, LibraryImageSrc>(StringComparer.Ordinal);
        var reused = 0;
        try
        {
            foreach (var image in images)
            {
                var upload = await uploads.UploadAsync(new MemoryStream(image.Decoded.Bytes!, writable: false), image.FileName, ct: ct);
                if (upload.Outcome == MediaUploadOutcome.Rejected)
                {
                    await RemoveAsync(createdIds);
                    return Failed(kind, id, [new EmbeddedImageRefusal(image.Name, upload.Rejection!)]);
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

                library[image.Src] = new LibraryImageSrc(asset.Id, urls.Original(asset, asset.Latest), asset.Latest.Width, asset.Latest.Height);
            }

            if (!await save(library, library.Values.Select(l => l.Id).Distinct().ToList()))
            {
                await RemoveAsync(createdIds);
                return Failed(kind, id, [new EmbeddedImageRefusal(title, EmbeddedImageProblems.ChangedWhileMoving)]);
            }

            logger.LogInformation("Embedded image move: {Kind} {Id} changed, {ImagesMoved} images moved", kind, id, imagesMoved);
            return new MoveOutcome(imagesMoved, createdIds.Count, reused, null);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Embedded image move: {Kind} {Id} left as it was", kind, id);
            await RemoveAsync(createdIds);
            return Failed(kind, id, [new EmbeddedImageRefusal(title, StoreFailed)]);
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

    private MoveOutcome Failed(string kind, object id, IReadOnlyList<EmbeddedImageRefusal> reasons)
    {
        logger.LogInformation("Embedded image move: {Kind} {Id} left as it was ({Count} problems)", kind, id, reasons.Count);
        return new MoveOutcome(0, 0, 0, reasons);
    }

    private sealed record VersionImages(EmbeddedImageVersion Version, IReadOnlyList<EmbeddedImage> Images);

    private sealed record PageImage(string Src, string? Alt, string Name, string FileName, DecodedDataImage Decoded);

    // The page's current versions that hold at least one embedded image.
    private async Task<List<VersionImages>> PageVersionsAsync(EmbeddedImagePage page, DateTime now, CancellationToken ct) =>
        (await store.GetCurrentVersionsAsync(page.NodeId, now, ct))
        .Select(v => new VersionImages(v, EmbeddedImageContent.Find(v.Content)))
        .Where(v => v.Images.Count > 0)
        .ToList();

    // Each different src in the page or block once, in document order, decoded. An image is named
    // by its alt text, then its title, then its position: "embedded-image-2".
    private static List<PageImage> Distinct(IEnumerable<EmbeddedImage> found)
    {
        var images = new List<PageImage>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var image in found)
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
