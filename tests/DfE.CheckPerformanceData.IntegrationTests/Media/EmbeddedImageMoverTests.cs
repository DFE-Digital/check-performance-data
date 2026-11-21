using System.Text;
using System.Text.Json.Nodes;
using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Application.Media;
using DfE.CheckPerformanceData.Application.Media.Embedded;
using DfE.CheckPerformanceData.Application.Media.Serving;
using DfE.CheckPerformanceData.Application.Media.Upload;
using DfE.CheckPerformanceData.Application.PageTree;
using DfE.CheckPerformanceData.Infrastructure.BlobStorage;
using DfE.CheckPerformanceData.Infrastructure.Media;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SkiaSharp;

namespace DfE.CheckPerformanceData.IntegrationTests.Media;

// Moving images embedded as data: URIs into the media library, end to end: a real database, real
// blob storage (Azurite) and the real upload pipeline. Only current page versions change, in place:
// no page version is added, a live version stays published and a draft stays a draft. Older
// versions keep their embedded images. Each page changes completely or not at all.
[Collection(nameof(PostgresCollection))]
public sealed class EmbeddedImageMoverTests(PostgresFixture postgres, AzuriteFixture azurite)
    : IClassFixture<AzuriteFixture>, IAsyncLifetime
{
    private readonly BlobServiceClient _blobs = new(azurite.ConnectionString);
    private readonly string _container = $"media-{Guid.NewGuid():N}";
    // The audit log cannot be cleared, so each test reads only the entries written after it started.
    private readonly DateTime _started = DateTime.UtcNow;

    private static readonly string Red = DataUri(Png(8, 8, SKColors.Red));
    private static readonly string Blue = DataUri(Png(6, 4, SKColors.Blue));
    private const string Broken = "data:image/png;base64,not*base64!";

    public async Task InitializeAsync()
    {
        await using var ctx = postgres.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync(
            @"TRUNCATE media_usages, media_asset_versions, media_assets, ""PageNodes"", ""PageNodeVersions"", ""ContentBlocks"", ""ContentBlockVersions"" RESTART IDENTITY CASCADE;");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ── helpers ──────────────────────────────────────────────────────────────

    private static byte[] Png(int width, int height, SKColor colour)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(colour);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return data!.ToArray();
    }

    private static string DataUri(byte[] png) => "data:image/png;base64," + Convert.ToBase64String(png);

    private static string Img(string src, string alt) => $"<img src=\"{src}\" alt=\"{alt}\">";

    private static string Tree(string html) => new JsonArray(new JsonObject
    {
        ["kind"] = "widget",
        ["type"] = "richtext",
        ["props"] = new JsonObject { ["html"] = html }
    }).ToJsonString();

    // The rich text of a one-widget tree. Stored JSON escapes quotes, so tests read the value.
    private static string Html(string content) => JsonNode.Parse(content)![0]!["props"]!["html"]!.GetValue<string>();

    private MediaBlobStore BlobStore => new(_blobs, _container);

    private EmbeddedImageMover Mover(PortalDbContext ctx)
    {
        var assets = new MediaAssetRepository(ctx, new FakeCurrentUserService());
        var uploads = new MediaUploadService(new MediaFileCleaner(new SkiaRasterImageCleaner()), assets, BlobStore,
            new MediaProcessingGate(Options.Create(new MediaProcessingOptions())), NullLogger<MediaUploadService>.Instance);
        return new EmbeddedImageMover(
            new EmbeddedImageStore(ctx), uploads, assets,
            new MediaDeletionService(assets, BlobStore, NullLogger<MediaDeletionService>.Instance),
            new MediaUrlBuilder(new MediaUrlSigner(new FixedKey())),
            new FakeCurrentUserService(), TimeProvider.System, NullLogger<EmbeddedImageMover>.Instance);
    }

    private async Task<EmbeddedImageScan> ScanAsync()
    {
        await using var ctx = postgres.CreateContext();
        return await Mover(ctx).ScanAsync();
    }

    private async Task<EmbeddedImageMoveResult> MoveAsync()
    {
        await using var ctx = postgres.CreateContext();
        return await Mover(ctx).MoveAsync();
    }

    private async Task<Guid> PageAsync(string segment, string pageType = "content")
    {
        await using var ctx = postgres.CreateContext();
        var node = await new PageNodeService(new PageNodeRepository(ctx))
            .CreatePageAsync(null, segment, $"Page {segment}", pageType, "test");
        return node.Id;
    }

    private async Task WithServiceAsync(Func<PageNodeService, Task> work)
    {
        await using var ctx = postgres.CreateContext();
        await work(new PageNodeService(new PageNodeRepository(ctx)));
    }

    private Task SaveDraftAsync(Guid page, string content) =>
        WithServiceAsync(s => s.SaveWorkingContentAsync(page, content, "", "test"));

    private Task PublishDraftAsync(Guid page) => WithServiceAsync(s => s.PublishDraftAsync(page, "test"));

    private async Task<List<PageNodeVersion>> VersionsAsync(Guid page)
    {
        await using var ctx = postgres.CreateContext();
        return await ctx.PageNodeVersions.AsNoTracking().Where(v => v.PageNodeId == page).OrderBy(v => v.VersionId).ToListAsync();
    }

    private async Task<List<MediaAsset>> LibraryAsync()
    {
        await using var ctx = postgres.CreateContext();
        return await ctx.MediaAssets.AsNoTracking().Include(a => a.Versions).OrderBy(a => a.FileName).ToListAsync();
    }

    // Page a: live v1 with Red twice, draft v2 with Red and Blue. Page c: Blue only in an older version.
    private async Task<Guid> PageWithLiveAndDraftAsync(string segment = "a-page")
    {
        var page = await PageAsync(segment);
        await SaveDraftAsync(page, Tree("<p>Intro</p>" + Img(Red, "Red square") + Img(Red, "Again")));
        await PublishDraftAsync(page);
        await SaveDraftAsync(page, Tree(Img(Red, "Red square") + Img(Blue, "")));
        return page;
    }

    private async Task<Guid> PageWithAnOlderVersionAsync()
    {
        var page = await PageAsync("c-page");
        await SaveDraftAsync(page, Tree(Img(Blue, "Old")));
        await PublishDraftAsync(page);
        await SaveDraftAsync(page, Tree("<p>No images now</p>"));
        await PublishDraftAsync(page);
        return page;
    }

    // ── dry run ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task TheDryRun_CountsPagesVersionsAndImages_AndListsWhatCannotMove()
    {
        var a = await PageWithLiveAndDraftAsync();
        var wiki = await PageAsync("b-wiki", "wiki");
        await SaveDraftAsync(wiki, "<p>Text</p>" + Img(Broken, "Broken chart"));
        await PageWithAnOlderVersionAsync();
        await using (var ctx = postgres.CreateContext())
        {
            ctx.ContentBlocks.Add(new ContentBlock
            {
                ContentId = Guid.NewGuid(), Key = "embedded-block", BlockType = "Content", Value = Img(Red, "Block"),
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
            });
            await ctx.SaveChangesAsync();
        }

        var scan = await ScanAsync();

        Assert.Collection(scan.Pages,
            p =>
            {
                Assert.Equal(a, p.Page.NodeId);
                Assert.Equal("a-page", p.Page.Path);
                Assert.Equal([EmbeddedVersionState.Live, EmbeddedVersionState.Draft], p.Versions);
                Assert.Equal(4, p.ImageCount);
                Assert.True(p.CanMove);
            },
            p =>
            {
                Assert.Equal(wiki, p.Page.NodeId);
                Assert.Equal("wiki", p.Page.PageType);
                Assert.Equal(1, p.ImageCount);
                var refusal = Assert.Single(p.Refusals);
                Assert.Equal(("Broken chart", EmbeddedImageProblems.Unreadable), (refusal.Image, refusal.Reason));
            });
        Assert.Equal(3, scan.VersionCount);
        Assert.Equal(6, scan.ImageCount);
        Assert.Equal(2, scan.UniqueImageCount);
        Assert.Equal(Png(8, 8, SKColors.Red).Length + Png(6, 4, SKColors.Blue).Length, scan.UniqueBytes);
        Assert.Equal(1, scan.PagesToMoveCount);
        // Four on the page and one in the block.
        Assert.Equal(5, scan.ImagesToMoveCount);
        Assert.Equal(1, scan.OlderVersionCount);
        var block = Assert.Single(scan.Blocks);
        Assert.Equal(("embedded-block", 1, true), (block.Block.Key, block.ImageCount, block.CanMove));
        Assert.Equal(1, scan.BlocksToMoveCount);

        // A dry run changes nothing.
        Assert.Empty(await LibraryAsync());
        Assert.Contains(Red, (await VersionsAsync(a))[0].Content);
    }

    // ── run ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ARun_StoresEachImageOnce_AndPointsTheCurrentVersionsAtTheLibrary_InPlace()
    {
        var a = await PageWithLiveAndDraftAsync();
        var d = await PageAsync("d-page");
        await SaveDraftAsync(d, Tree(Img(Red, "Same red")));
        var c = await PageWithAnOlderVersionAsync();
        var before = await VersionsAsync(a);

        var result = await MoveAsync();

        Assert.Equal(2, result.PagesChanged);
        Assert.Equal(5, result.ImagesMoved);
        Assert.Equal(2, result.ImagesCreated);
        Assert.Equal(1, result.DuplicatesReused);
        Assert.Empty(result.Failures);

        var library = await LibraryAsync();
        Assert.Equal(2, library.Count);
        var red = Assert.Single(library, x => x.FileName == "Red square.png");
        Assert.Equal("Red square", red.DefaultAltText);
        var blue = Assert.Single(library, x => x.FileName == "embedded-image-2.png");
        Assert.Null(blue.DefaultAltText);

        // No version is added. The live version stays published, unchanged but for its images.
        var after = await VersionsAsync(a);
        Assert.Equal(before.Select(v => v.Id), after.Select(v => v.Id));
        var (live, draft) = (after[0], after[1]);
        Assert.Equal(before[0].PublishFrom, live.PublishFrom);
        Assert.Equal(0, live.MinorVersion);
        Assert.True(live.IsCurrent);
        Assert.DoesNotContain("data:", live.Content);
        var redSrc = $"src=\"/media/{red.Id}/1/red-square.png\" data-media-id=\"{red.Id}\"";
        var liveHtml = Html(live.Content);
        Assert.Equal($"<p>Intro</p><img {redSrc} alt=\"Red square\"><img {redSrc} alt=\"Again\">", liveHtml);

        // The draft is saved as an editor's save is: its minor number goes up.
        Assert.Null(draft.PublishFrom);
        Assert.Equal(before[1].MinorVersion + 1, draft.MinorVersion);
        Assert.Contains($"data-media-id=\"{blue.Id}\"", Html(draft.Content));
        Assert.DoesNotContain("data:", draft.Content);

        Assert.Contains($"data-media-id=\"{red.Id}\"", Html((await VersionsAsync(d))[0].Content));

        // The older version keeps its embedded image.
        Assert.Contains(Blue, (await VersionsAsync(c))[0].Content);

        await using var ctx = postgres.CreateContext();
        var usages = await ctx.MediaUsages.AsNoTracking().Where(u => u.PageNodeId == a)
            .Select(u => new { u.PageNodeVersionId, u.AssetId, u.AssetVersion }).ToListAsync();
        Assert.Equal(3, usages.Count);
        Assert.Contains(usages, u => u.PageNodeVersionId == live.Id && u.AssetId == red.Id && u.AssetVersion == 1);
        Assert.Contains(usages, u => u.PageNodeVersionId == draft.Id && u.AssetId == red.Id);
        Assert.Contains(usages, u => u.PageNodeVersionId == draft.Id && u.AssetId == blue.Id);

        var audit = await ctx.AuditEntries.AsNoTracking().Where(e => e.EntityType == "EmbeddedImageMove" && e.Timestamp >= _started).ToListAsync();
        Assert.Equal(new[] { a, d }.Select(id => id.ToString()).Order(), audit.Where(e => e.Action == "PageChanged").Select(e => e.EntityId).Order());
        var summary = Assert.Single(audit, e => e.Action == "Run");
        Assert.Equal("test-user", summary.UserId);
        Assert.Contains("\"pagesChanged\":2", summary.NewValues);
    }

    [Fact]
    public async Task ScheduledVersions_ChangeInPlace_AndStayScheduled()
    {
        var page = await PageAsync("scheduled-page");
        await SaveDraftAsync(page, Tree(Img(Red, "Live")));
        await PublishDraftAsync(page);
        await SaveDraftAsync(page, Tree(Img(Blue, "Soon")));
        var from = DateTime.UtcNow.AddDays(7);
        await WithServiceAsync(s => s.PublishAsync(page, 2, from, null, "test"));

        var scan = await ScanAsync();
        Assert.Equal([EmbeddedVersionState.Live, EmbeddedVersionState.Scheduled], Assert.Single(scan.Pages).Versions);

        await MoveAsync();

        var versions = await VersionsAsync(page);
        Assert.Equal(2, versions.Count);
        Assert.All(versions, v => Assert.DoesNotContain("data:", v.Content));
        Assert.Equal(from, versions[1].PublishFrom!.Value, TimeSpan.FromMilliseconds(1));
        Assert.Equal(0, versions[1].MinorVersion);
    }

    [Fact]
    public async Task AWikiBody_IsChangedOnlyInItsImageTags()
    {
        var wiki = await PageAsync("wiki-page", "wiki");
        var body = "# Heading\n\nSome *markdown* &amp; text\n\n" + Img(Red, "Chart") + "\n\n| a | b |";
        await SaveDraftAsync(wiki, body);

        await MoveAsync();

        var red = Assert.Single(await LibraryAsync());
        var saved = Assert.Single(await VersionsAsync(wiki)).Content;
        Assert.Equal(body.Replace($"src=\"{Red}\"", $"src=\"/media/{red.Id}/1/chart.png\" data-media-id=\"{red.Id}\""), saved);
    }

    [Fact]
    public async Task ASecondRun_FindsNothingToDo()
    {
        await PageWithLiveAndDraftAsync();
        await MoveAsync();

        var scan = await ScanAsync();
        var second = await MoveAsync();

        Assert.Empty(scan.Pages);
        Assert.Equal(0, scan.ImageCount);
        Assert.Equal((0, 0, 0, 0), (second.PagesChanged, second.ImagesMoved, second.ImagesCreated, second.DuplicatesReused));
        Assert.Empty(second.Failures);
        Assert.Equal(2, (await LibraryAsync()).Count);
    }

    // ── failures ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task AnImageTheLibraryRefuses_LeavesItsWholePageAsItWas_AndOtherPagesStillMove()
    {
        // The markup makes it an SVG candidate; the SVG sanitiser cannot parse it.
        const string badSvg = "data:image/svg+xml,%3Csvg%20xmlns%3D%22http%3A%2F%2Fwww.w3.org%2F2000%2Fsvg%22%3E%3Cunclosed";
        var e = await PageAsync("e-page");
        var content = Tree(Img(Red, "Fine") + Img(badSvg, "Bad drawing"));
        await SaveDraftAsync(e, content);
        var f = await PageAsync("f-page");
        await SaveDraftAsync(f, Tree(Img(Blue, "Blue")));

        var result = await MoveAsync();

        var failure = Assert.Single(result.Failures);
        Assert.Equal(e, failure.Page.NodeId);
        Assert.Equal(("Bad drawing", MediaRejections.SvgUnreadable), (failure.Reasons[0].Image, failure.Reasons[0].Reason));
        Assert.Equal(content, Assert.Single(await VersionsAsync(e)).Content);
        Assert.Equal(1, result.PagesChanged);
        Assert.Equal(1, result.ImagesCreated);

        // The red image was stored for page e, then removed again when the page failed.
        var library = await LibraryAsync();
        Assert.Equal("Blue.png", Assert.Single(library).FileName);
        await using var ctx = postgres.CreateContext();
        Assert.False(await ctx.MediaUsages.AnyAsync(u => u.PageNodeId == e));
    }

    [Fact]
    public async Task AnImageThatCannotBeRead_StopsItsPageBeforeAnythingIsStored()
    {
        var page = await PageAsync("g-page");
        var content = Tree(Img(Red, "Fine") + Img(Broken, "Broken"));
        await SaveDraftAsync(page, content);

        var result = await MoveAsync();

        Assert.Equal(EmbeddedImageProblems.Unreadable, Assert.Single(Assert.Single(result.Failures).Reasons).Reason);
        Assert.Equal(content, Assert.Single(await VersionsAsync(page)).Content);
        Assert.Empty(await LibraryAsync());
    }

    [Fact]
    public async Task AnEmbeddedSvg_GoesThroughTheSvgSanitiser()
    {
        const string svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"10\" height=\"5\" onload=\"alert(1)\">" +
                           "<script>alert(2)</script><rect width=\"10\" height=\"5\" fill=\"red\"/></svg>";
        var page = await PageAsync("svg-page");
        await SaveDraftAsync(page, Tree(Img("data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(svg)), "Logo")));

        var result = await MoveAsync();

        Assert.Equal(1, result.PagesChanged);
        var asset = Assert.Single(await LibraryAsync());
        Assert.Equal("Logo.svg", asset.FileName);
        var version = Assert.Single(asset.Versions);
        Assert.Equal("image/svg+xml", version.ContentType);
        await using var blob = await BlobStore.GetAsync(version.BlobKey);
        var stored = await new StreamReader(blob!.Content).ReadToEndAsync();
        Assert.Contains("<rect", stored);
        Assert.DoesNotContain("script", stored, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onload", stored, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"src=\"/media/{asset.Id}/1/logo.svg\"", Html(Assert.Single(await VersionsAsync(page)).Content));
    }

    [Fact]
    public async Task SavingAPage_ChangesNothing_WhenAnEditorSavedItMeanwhile()
    {
        var page = await PageAsync("h-page");
        await SaveDraftAsync(page, Tree(Img(Red, "x")));
        var version = Assert.Single(await VersionsAsync(page));
        await SaveDraftAsync(page, Tree("<p>The editor's newer text</p>"));

        bool saved;
        await using (var ctx = postgres.CreateContext())
        {
            saved = await new EmbeddedImageStore(ctx).SavePageAsync(new EmbeddedImagePageSave(
                new EmbeddedImagePage(page, "Page", "h-page", "content"),
                [new EmbeddedImageVersionChange(version.Id, version.Content, Tree("<p>moved</p>"))],
                1, []), "test-user");
        }

        Assert.False(saved);
        Assert.Contains("The editor's newer text", Html(Assert.Single(await VersionsAsync(page)).Content));
        await using var read = postgres.CreateContext();
        Assert.False(await read.AuditEntries.AnyAsync(a => a.EntityType == "EmbeddedImageMove" && a.Timestamp >= _started));
    }

    // ── content blocks ───────────────────────────────────────────────────────

    private async Task<int> BlockAsync(string key, string value)
    {
        await using var ctx = postgres.CreateContext();
        var service = new Application.ContentBlocks.ContentBlockService(
            new ContentBlockRepository(ctx), new Application.Common.HtmlRenderingService());
        var block = await service.SaveAsync(new Application.ContentBlocks.SaveContentBlockDto { Key = key, BlockType = "Content", Value = value });
        return block.Id;
    }

    private async Task<ContentBlock> ReadBlockAsync(int id)
    {
        await using var ctx = postgres.CreateContext();
        return await ctx.ContentBlocks.AsNoTracking().Include(b => b.Versions).SingleAsync(b => b.Id == id);
    }

    // A content block's usages are recorded, so its embedded images move like a page's. The block
    // changes in place and gets a new version in its history, as an editor's save does.
    [Fact]
    public async Task ARun_MovesTheImagesInContentBlocks_AndRecordsTheirUsages()
    {
        var page = await PageAsync("k-page");
        await SaveDraftAsync(page, Tree(Img(Red, "Red square")));
        var blockId = await BlockAsync("embedded-block", "<p>Block</p>" + Img(Red, "Block red") + Img(Blue, "Block blue"));

        var result = await MoveAsync();

        Assert.Equal(1, result.PagesChanged);
        Assert.Equal(1, result.BlocksChanged);
        Assert.Equal(3, result.ImagesMoved);
        Assert.Equal(2, result.ImagesCreated);
        Assert.Equal(1, result.DuplicatesReused);
        Assert.Empty(result.BlockFailures);

        var library = await LibraryAsync();
        var red = Assert.Single(library, x => x.FileName == "Red square.png");
        var blue = Assert.Single(library, x => x.FileName == "Block blue.png");
        var block = await ReadBlockAsync(blockId);
        Assert.Equal(
            $"<p>Block</p><img src=\"/media/{red.Id}/1/red-square.png\" data-media-id=\"{red.Id}\" alt=\"Block red\">"
            + $"<img src=\"/media/{blue.Id}/1/block-blue.png\" data-media-id=\"{blue.Id}\" alt=\"Block blue\">",
            block.Value);
        Assert.Equal([1, 2], block.Versions.OrderBy(v => v.VersionNumber).Select(v => v.VersionNumber));
        Assert.Equal(block.Value, block.Versions.Single(v => v.VersionNumber == 2).Value);

        await using var ctx = postgres.CreateContext();
        Assert.Equal(
            new[] { red.Id, blue.Id }.Order(),
            await ctx.MediaUsages.Where(u => u.ContentBlockId == blockId).Select(u => u.AssetId).OrderBy(id => id).ToListAsync());
        var audit = Assert.Single(await ctx.AuditEntries.AsNoTracking()
            .Where(e => e.EntityType == "EmbeddedImageMove" && e.Action == "ContentBlockChanged" && e.Timestamp >= _started).ToListAsync());
        Assert.Equal(blockId.ToString(), audit.EntityId);
        Assert.Contains("\"key\":\"embedded-block\"", audit.NewValues);
        var summary = Assert.Single(await ctx.AuditEntries.AsNoTracking()
            .Where(e => e.EntityType == "EmbeddedImageMove" && e.Action == "Run" && e.Timestamp >= _started).ToListAsync());
        Assert.Contains("\"blocksChanged\":1", summary.NewValues);

        // A second run finds nothing left.
        var scan = await ScanAsync();
        Assert.Empty(scan.Blocks);
    }

    [Fact]
    public async Task ABlockWithAnImageThatCannotMove_IsLeftAsItWas()
    {
        var value = Img(Red, "Fine") + Img(Broken, "Broken chart");
        var blockId = await BlockAsync("broken-block", value);

        var result = await MoveAsync();

        var failure = Assert.Single(result.BlockFailures);
        Assert.Equal(("broken-block", "Broken chart", EmbeddedImageProblems.Unreadable),
            (failure.Block.Key, failure.Reasons[0].Image, failure.Reasons[0].Reason));
        Assert.Equal(0, result.BlocksChanged);
        Assert.Equal(value, (await ReadBlockAsync(blockId)).Value);
        Assert.Empty(await LibraryAsync());
    }

    [Fact]
    public async Task SavingABlock_ChangesNothing_WhenAnEditorSavedItMeanwhile()
    {
        var value = Img(Red, "x");
        var blockId = await BlockAsync("busy-block", value);
        await BlockAsync("busy-block", "<p>The editor's newer text</p>");

        bool saved;
        await using (var ctx = postgres.CreateContext())
        {
            saved = await new EmbeddedImageStore(ctx).SaveContentBlockAsync(new EmbeddedImageBlockSave(
                new EmbeddedImageBlock(blockId, "busy-block", value), "<p>moved</p>", 1, []), "test-user");
        }

        Assert.False(saved);
        Assert.Equal("<p>The editor's newer text</p>", (await ReadBlockAsync(blockId)).Value);
    }

    private sealed class FixedKey : IMediaUrlSigningKey
    {
        public byte[] GetKey() => Enumerable.Repeat((byte)5, 32).ToArray();
    }
}
