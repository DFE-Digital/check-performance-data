using System.Text.Json.Nodes;
using DfE.CheckPerformanceData.Application.Common;
using DfE.CheckPerformanceData.Application.ContentBlocks;
using DfE.CheckPerformanceData.Application.Media;
using DfE.CheckPerformanceData.Application.Media.Embedded;
using DfE.CheckPerformanceData.Application.PageTree;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.IntegrationTests.Media;

// The queries behind the media reports, against a real database. The accessibility report reads
// the current page versions and content blocks that record a use of a library image; older
// versions and deleted pages are left out. The size report adds up the stored versions.
[Collection(nameof(PostgresCollection))]
public sealed class MediaReportStoreTests(PostgresFixture fixture) : IAsyncLifetime
{
    private readonly List<Guid> _pages = [];

    public async Task InitializeAsync()
    {
        await using var ctx = fixture.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync(
            @"TRUNCATE media_usages, media_asset_versions, media_assets, ""ContentBlocks"", ""ContentBlockVersions"" RESTART IDENTITY CASCADE");
    }

    public async Task DisposeAsync()
    {
        await using var ctx = fixture.CreateContext();
        await ctx.MediaUsages.Where(u => u.PageNodeId != null && _pages.Contains(u.PageNodeId.Value)).ExecuteDeleteAsync();
        await ctx.PageNodeVersions.Where(v => _pages.Contains(v.PageNodeId)).ExecuteDeleteAsync();
        await ctx.PageNodes.IgnoreQueryFilters().Where(n => _pages.Contains(n.Id)).ExecuteDeleteAsync();
    }

    private static string Image(Guid id, string alt) =>
        new JsonArray(new JsonObject
        {
            ["kind"] = "widget",
            ["type"] = "image",
            ["props"] = new JsonObject { ["mediaId"] = id.ToString(), ["alt"] = alt },
        }).ToJsonString();

    private static string Text() =>
        new JsonArray(new JsonObject
        {
            ["kind"] = "widget",
            ["type"] = "richtext",
            ["props"] = new JsonObject { ["html"] = "<p>No images</p>" },
        }).ToJsonString();

    private static NewMediaAssetVersion File(Guid id, int version, long size) =>
        new(MediaBlobKeys.Original(id, version), "image/png", 640, 480, size, new string((char)('a' + version), 64), IsAnimated: false);

    private async Task<Guid> ImageAsync(string fileName, string? alt, params long[] sizes)
    {
        var id = Guid.NewGuid();
        await using var ctx = fixture.CreateContext();
        var repo = new MediaAssetRepository(ctx, new FakeCurrentUserService());
        var metadata = MediaAssetMetadata.Empty with { DefaultAltText = alt, Caption = "A caption" };
        await repo.CreateAsync(new NewMediaAsset(id, fileName, "slug", metadata, File(id, 1, sizes[0])));
        for (var v = 2; v <= sizes.Length; v++) await repo.AddVersionAsync(id, v, File(id, v, sizes[v - 1]));
        return id;
    }

    private async Task<Guid> PageAsync(string title)
    {
        await using var ctx = fixture.CreateContext();
        var node = await new PageNodeService(new PageNodeRepository(ctx))
            .CreatePageAsync(null, $"report-{Guid.NewGuid():N}", title, "content", "test");
        _pages.Add(node.Id);
        return node.Id;
    }

    private async Task WithServiceAsync(Func<PageNodeService, Task> work)
    {
        await using var ctx = fixture.CreateContext();
        await work(new PageNodeService(new PageNodeRepository(ctx)));
    }

    private Task SaveDraftAsync(Guid page, string content) =>
        WithServiceAsync(s => s.SaveWorkingContentAsync(page, content, "", "test"));

    private Task PublishDraftAsync(Guid page) => WithServiceAsync(s => s.PublishDraftAsync(page, "test"));

    private async Task<T> WithStoreAsync<T>(Func<MediaReportStore, Task<T>> read)
    {
        await using var ctx = fixture.CreateContext();
        return await read(new MediaReportStore(ctx));
    }

    [Fact]
    public async Task ListAssets_GivesEveryImage_WithItsDefaultTextAndCaption()
    {
        var a = await ImageAsync("a.png", "photo", 10);
        var b = await ImageAsync("b.png", null, 10);

        var assets = await WithStoreAsync(s => s.ListAssetsAsync());

        Assert.Equal(2, assets.Count);
        var first = assets.Single(x => x.Id == a);
        Assert.Equal(("a.png", "photo", "A caption"), (first.FileName, first.DefaultAltText, first.Caption));
        Assert.Null(assets.Single(x => x.Id == b).DefaultAltText);
    }

    [Fact]
    public async Task CurrentPageVersions_AreTheLiveAndDraftVersionsThatUseAnImage_WithTheirContent()
    {
        var image = await ImageAsync("a.png", null, 10);
        var page = await PageAsync("Uses the image");
        await SaveDraftAsync(page, Image(image, "live text"));
        await PublishDraftAsync(page);
        await SaveDraftAsync(page, Image(image, "draft text"));

        var versions = await WithStoreAsync(s => s.ListCurrentPageVersionsAsync(DateTime.UtcNow));

        var mine = versions.Where(v => v.PageNodeId == page).OrderBy(v => v.State).ToList();
        Assert.Equal([EmbeddedVersionState.Live, EmbeddedVersionState.Draft], mine.Select(v => v.State));
        Assert.Contains("live text", mine[0].Content);
        Assert.Contains("draft text", mine[1].Content);
        Assert.All(mine, v => Assert.Equal("Uses the image", v.Title));
    }

    [Fact]
    public async Task OlderVersions_PagesWithNoImage_AndDeletedPages_AreLeftOut()
    {
        var image = await ImageAsync("a.png", null, 10);

        var replaced = await PageAsync("Image taken out");
        await SaveDraftAsync(replaced, Image(image, "old text"));
        await PublishDraftAsync(replaced);
        await SaveDraftAsync(replaced, Text());
        await PublishDraftAsync(replaced);

        var deleted = await PageAsync("Deleted page");
        await SaveDraftAsync(deleted, Image(image, "deleted text"));
        await using (var ctx = fixture.CreateContext())
        {
            await ctx.PageNodes.Where(n => n.Id == deleted).ExecuteUpdateAsync(s => s.SetProperty(n => n.DeletedDate, DateTime.UtcNow));
        }

        var versions = await WithStoreAsync(s => s.ListCurrentPageVersionsAsync(DateTime.UtcNow));

        Assert.DoesNotContain(versions, v => v.PageNodeId == replaced || v.PageNodeId == deleted);
    }

    [Fact]
    public async Task ContentBlocks_AreTheBlocksThatUseAnImage_WithTheirValue()
    {
        var image = await ImageAsync("a.png", null, 10);
        await using (var ctx = fixture.CreateContext())
        {
            var service = new ContentBlockService(new ContentBlockRepository(ctx), new HtmlRenderingService());
            await service.SaveAsync(new SaveContentBlockDto { Key = "report-with-image", BlockType = "Content", Value = $"<img data-media-id=\"{image}\" alt=\"logo\">" });
            await service.SaveAsync(new SaveContentBlockDto { Key = "report-no-image", BlockType = "Content", Value = "<p>Text</p>" });
        }

        var blocks = await WithStoreAsync(s => s.ListContentBlocksAsync());

        var block = Assert.Single(blocks);
        Assert.Equal("report-with-image", block.Key);
        Assert.Contains("alt=\"logo\"", block.Value);
    }

    [Fact]
    public async Task SizeTotals_AddUpTheCurrentFiles_AndEveryStoredVersion()
    {
        await ImageAsync("a.png", null, 100, 300);
        await ImageAsync("b.png", null, 50);

        var totals = await WithStoreAsync(s => s.GetSizeTotalsAsync());

        Assert.Equal(new Application.Media.Reports.MediaSizeTotals(2, 3, 350, 450), totals);
    }

    [Fact]
    public async Task SizeTotals_OfAnEmptyLibrary_AreZero()
    {
        Assert.Equal(new Application.Media.Reports.MediaSizeTotals(0, 0, 0, 0), await WithStoreAsync(s => s.GetSizeTotalsAsync()));
    }

    [Fact]
    public async Task Largest_PutsTheBiggestCurrentFileFirst_AndCountsVersions()
    {
        var a = await ImageAsync("a.png", null, 900, 100);
        var b = await ImageAsync("b.png", null, 500);
        var c = await ImageAsync("c.png", null, 50);

        var largest = await WithStoreAsync(s => s.ListLargestAsync(2));

        Assert.Equal([b, a], largest.Select(x => x.Id));
        Assert.Equal(new Application.Media.Reports.MediaSizeAsset(a, "a.png", "image/png", 2, 100, 2, 1_000), largest[1]);
        Assert.DoesNotContain(largest, x => x.Id == c);
    }
}
