using System.Text.Json.Nodes;
using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Application.Common;
using DfE.CheckPerformanceData.Application.ContentStaging;
using DfE.CheckPerformanceData.Application.Media;
using DfE.CheckPerformanceData.Application.Media.Upload;
using DfE.CheckPerformanceData.Application.PageTree;
using DfE.CheckPerformanceData.Infrastructure.BlobStorage;
using DfE.CheckPerformanceData.Infrastructure.Media;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SkiaSharp;

namespace DfE.CheckPerformanceData.IntegrationTests.ContentStaging;

// Content staging with media library images, against a real database and real blob storage
// (Azurite). An export carries the images its pages and blocks use; importing it into an empty
// environment brings each image in under its own id, with the image versions that the page
// history was saved with, and rebuilds the usages of the imported page versions and blocks.
[Collection(nameof(PostgresCollection))]
public sealed class ContentStagingMediaTests(PostgresFixture postgres, AzuriteFixture azurite)
    : IClassFixture<AzuriteFixture>, IAsyncLifetime
{
    private readonly BlobServiceClient _blobs = new(azurite.ConnectionString);
    private readonly string _source = $"media-{Guid.NewGuid():N}";
    private readonly string _target = $"media-{Guid.NewGuid():N}";

    public Task InitializeAsync() => WipeAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task WipeAsync()
    {
        await using var ctx = postgres.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync(
            @"TRUNCATE media_usages, media_asset_versions, media_assets, ""PageNodes"", ""PageNodeVersions"", ""ContentBlocks"", ""ContentBlockVersions"", ""HomeBanners"", ""HomeBannerVersions"" RESTART IDENTITY CASCADE;");
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static byte[] Png(int width, int height, SKColor colour)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(colour);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return data!.ToArray();
    }

    private static string ImageWidget(Guid id) => new JsonArray(new JsonObject
    {
        ["kind"] = "widget",
        ["type"] = "image",
        ["props"] = new JsonObject { ["mediaId"] = id.ToString(), ["alt"] = "A picture" }
    }).ToJsonString();

    private static string LibraryImg(Guid id) => $"<p><img data-media-id=\"{id}\" src=\"/media/{id}/q.png\" alt=\"q\"></p>";

    private static MediaProcessingGate Gate => new(Options.Create(new MediaProcessingOptions()));

    private MediaUploadService Uploads(PortalDbContext ctx, string container) => new(
        new MediaFileCleaner(new SkiaRasterImageCleaner()), new MediaAssetRepository(ctx, new FakeCurrentUserService()),
        new MediaBlobStore(_blobs, container), Gate, NullLogger<MediaUploadService>.Instance);

    private ContentStagingService Staging(PortalDbContext ctx, string container) => new(
        new PageNodeRepository(ctx), new ContentBlockRepository(ctx), new HomeBannerRepository(ctx, new FakeCurrentUserService()),
        new HtmlRenderingService(),
        media: new ContentMediaStaging(
            new MediaAssetRepository(ctx, new FakeCurrentUserService()), new MediaBlobStore(_blobs, container),
            new MediaFileCleaner(new SkiaRasterImageCleaner()), Gate, NullLogger<ContentMediaStaging>.Instance));

    private async Task<Guid> UploadAsync(string container, byte[] png, string name)
    {
        await using var ctx = postgres.CreateContext();
        var result = await Uploads(ctx, container).UploadAsync(new MemoryStream(png), name);
        return result.Asset!.Id;
    }

    private async Task<ContentBundle> ExportAsync(string container)
    {
        await using var ctx = postgres.CreateContext();
        var bundle = await Staging(ctx, container).ExportAsync(new ContentExportSelection(MaxVersionsPerNode: null));
        // Through the wire format, as an upload would arrive.
        return ContentStagingJson.Deserialize(ContentStagingJson.Serialize(bundle))!;
    }

    private async Task<ContentImportResult> ImportAsync(ContentBundle bundle, string container)
    {
        await using var ctx = postgres.CreateContext();
        return await Staging(ctx, container).ImportAsync(bundle, ContentImportMode.Replace);
    }

    private async Task<byte[]?> BlobAsync(string container, string key)
    {
        var blob = _blobs.GetBlobContainerClient(container).GetBlobClient(key);
        if (!await blob.ExistsAsync()) return null;
        return (await blob.DownloadContentAsync()).Value.Content.ToArray();
    }

    // A content page with an Image widget, two versions: version 1 is history pinned to the
    // image's first file, version 2 shows its current (second) file. A wiki page and a Content
    // block use a second image in rich text.
    private async Task<(Guid Photo, Guid Chart, Guid Page)> SeedSourceAsync()
    {
        var photo = await UploadAsync(_source, Png(8, 4, SKColors.Red), "photo.png");
        var chart = await UploadAsync(_source, Png(6, 6, SKColors.Green), "chart.png");

        await using var ctx = postgres.CreateContext();
        var service = new PageNodeService(new PageNodeRepository(ctx));
        var page = await service.CreatePageAsync(null, "pictures", "Pictures", "content", "seed");
        await service.SaveWorkingContentAsync(page.Id, ImageWidget(photo), "", "seed");
        await service.PublishDraftAsync(page.Id, "seed");
        await service.SaveWorkingContentAsync(page.Id, ImageWidget(photo), "", "seed");
        await service.PublishDraftAsync(page.Id, "seed");

        var wiki = await service.CreatePageAsync(null, "notes", "Notes", "wiki", "seed");
        await service.SaveWorkingContentAsync(wiki.Id, LibraryImg(chart), "", "seed");
        await service.PublishDraftAsync(wiki.Id, "seed");
        await new ContentBlockRepository(ctx).AddBlockAsync("chart-block", "Content", LibraryImg(chart), "");

        var replaced = await Uploads(ctx, _source).ReplaceAsync(photo, new MemoryStream(Png(8, 4, SKColors.Blue)));
        Assert.Equal(MediaReplaceOutcome.Replaced, replaced.Outcome);

        // Version 1 is history: it keeps showing the first file.
        var first = await ctx.PageNodeVersions.SingleAsync(v => v.PageNodeId == page.Id && v.VersionId == 1);
        await ctx.MediaUsages.Where(u => u.PageNodeVersionId == first.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.AssetVersion, 1));
        return (photo, chart, page.Id);
    }

    // ── tests ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Export_carries_each_used_image_with_its_pinned_and_current_files_once_each()
    {
        var (photo, chart, _) = await SeedSourceAsync();

        var bundle = await ExportAsync(_source);

        Assert.Equal(new[] { photo, chart }.Order(), bundle.MediaAssets.Select(a => a.Id).Order());
        Assert.Equal([1, 2], bundle.MediaAssets.Single(a => a.Id == photo).Versions.Select(v => v.Version));
        Assert.Equal(3, bundle.MediaFiles.Count);
        var page = bundle.PageNodes.Single(p => p.Segment == "pictures");
        Assert.Equal(1, page.Versions.Single(v => v.VersionId == 1).Media.Single().Version);
        Assert.Equal(2, page.Versions.Single(v => v.VersionId == 2).Media.Single().Version);
    }

    [Fact]
    public async Task Import_into_an_empty_environment_keeps_ids_files_history_pins_and_usages()
    {
        var (photo, chart, pageId) = await SeedSourceAsync();
        var bundle = await ExportAsync(_source);
        await WipeAsync();

        var result = await ImportAsync(bundle, _target);

        Assert.Empty(result.Errors);
        Assert.Empty(result.Warnings);
        Assert.Equal(2, result.MediaAssetsCreated);

        await using var ctx = postgres.CreateContext();
        var photoRow = await ctx.MediaAssets.SingleAsync(a => a.Id == photo);
        Assert.Equal(2, photoRow.CurrentVersion);
        Assert.Equal("photo.png", photoRow.FileName);
        foreach (var version in await ctx.MediaAssetVersions.Where(v => v.AssetId == photo || v.AssetId == chart).ToListAsync())
        {
            var stored = await BlobAsync(_target, version.BlobKey);
            Assert.NotNull(stored);
            Assert.Equal(version.Sha256, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(stored)));
        }

        // Every exported file is the same file in the target, so the history shows the same pictures.
        var sourceFiles = bundle.MediaAssets.Single(a => a.Id == photo).Versions.ToDictionary(v => v.Version, v => v.Sha256);
        var targetFiles = await ctx.MediaAssetVersions.Where(v => v.AssetId == photo).ToDictionaryAsync(v => v.Version, v => v.Sha256);
        Assert.Equal(sourceFiles[1], targetFiles[1]);
        Assert.Equal(sourceFiles[2], targetFiles[2]);

        var pins = await (from u in ctx.MediaUsages
                          join v in ctx.PageNodeVersions on u.PageNodeVersionId equals v.Id
                          where v.PageNodeId == pageId
                          select new { v.VersionId, u.AssetId, u.AssetVersion }).ToListAsync();
        Assert.Equal(2, pins.Count);
        Assert.All(pins, p => Assert.Equal(photo, p.AssetId));
        Assert.Equal(1, pins.Single(p => p.VersionId == 1).AssetVersion);
        Assert.Equal(2, pins.Single(p => p.VersionId == 2).AssetVersion);

        Assert.True(await ctx.MediaUsages.AnyAsync(u => u.AssetId == chart && u.ContentBlockId != null));
        Assert.True(await ctx.MediaUsages.AnyAsync(u => u.AssetId == chart && u.PageNodeVersionId != null));
    }

    [Fact]
    public async Task Importing_the_same_bundle_again_reuses_every_image()
    {
        var (photo, _, _) = await SeedSourceAsync();
        var bundle = await ExportAsync(_source);
        await WipeAsync();
        await ImportAsync(bundle, _target);

        var again = await ImportAsync(bundle, _target);

        Assert.Equal(0, again.MediaAssetsCreated);
        Assert.Equal(0, again.MediaAssetsUpdated);
        Assert.Equal(2, again.MediaAssetsReused);
        await using var ctx = postgres.CreateContext();
        Assert.Equal(2, (await ctx.MediaAssets.SingleAsync(a => a.Id == photo)).CurrentVersion);
    }

    [Fact]
    public async Task Importing_into_the_environment_it_came_from_reuses_every_image()
    {
        var (photo, chart, _) = await SeedSourceAsync();
        var bundle = await ExportAsync(_source);

        var result = await ImportAsync(bundle, _source);

        Assert.Empty(result.Warnings);
        Assert.Equal(2, result.MediaAssetsReused);
        await using var ctx = postgres.CreateContext();
        Assert.Equal(3, await ctx.MediaAssetVersions.CountAsync(v => v.AssetId == photo || v.AssetId == chart));
    }

    [Fact]
    public async Task A_bundle_without_media_still_imports_and_its_pages_use_the_library_copy()
    {
        var (photo, _, pageId) = await SeedSourceAsync();
        var bundle = await ExportAsync(_source);
        var oldShape = new ContentBundle { PageNodes = bundle.PageNodes, ContentBlocks = bundle.ContentBlocks };
        foreach (var page in oldShape.PageNodes)
            for (var i = 0; i < page.Versions.Count; i++)
                page.Versions[i] = page.Versions[i] with { Media = [] };
        await using (var ctx = postgres.CreateContext())
            await ctx.Database.ExecuteSqlRawAsync(@"TRUNCATE ""PageNodes"", ""PageNodeVersions"", ""ContentBlocks"", ""ContentBlockVersions"" RESTART IDENTITY CASCADE;");

        var result = await ImportAsync(oldShape, _source);

        Assert.Empty(result.Errors);
        Assert.Empty(result.Warnings);
        await using var check = postgres.CreateContext();
        var pins = await check.MediaUsages.Where(u => u.PageNodeId == pageId).Select(u => u.AssetVersion).ToListAsync();
        Assert.Equal([2, 2], pins);
        Assert.Equal(2, (await check.MediaAssets.SingleAsync(a => a.Id == photo)).CurrentVersion);
    }

    [Fact]
    public async Task A_bundle_whose_image_is_missing_imports_the_page_with_a_warning()
    {
        var (photo, _, _) = await SeedSourceAsync();
        var bundle = await ExportAsync(_source);
        bundle.MediaAssets.RemoveAll(a => a.Id == photo);
        await WipeAsync();

        var result = await ImportAsync(bundle, _target);

        Assert.Empty(result.Errors);
        Assert.Equal(2, result.PageNodesCreated);
        Assert.Contains(result.Warnings, w => w.Contains("‘Pictures’") && w.Contains(photo.ToString()));
    }
}
