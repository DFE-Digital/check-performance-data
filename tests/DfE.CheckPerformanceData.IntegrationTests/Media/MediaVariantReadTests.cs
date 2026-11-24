using System.Text.Json.Nodes;
using DfE.CheckPerformanceData.Application.Common;
using DfE.CheckPerformanceData.Application.ContentBlocks;
using DfE.CheckPerformanceData.Application.Media;
using DfE.CheckPerformanceData.Application.Media.Embedded;
using DfE.CheckPerformanceData.Application.Media.Variants;
using DfE.CheckPerformanceData.Application.PageTree;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.IntegrationTests.Media;

// How current content shows an image, read from real page versions and content blocks. The
// settings of each use are in the content, so they are read from the versions that media_usages
// records: the live, scheduled and draft versions of pages that are not deleted, and blocks.
[Collection(nameof(PostgresCollection))]
public sealed class MediaVariantReadTests(PostgresFixture fixture) : IAsyncLifetime
{
    private readonly List<Guid> _pages = [];
    private readonly List<string> _blocks = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var ctx = fixture.CreateContext();
        await ctx.MediaUsages.Where(u => u.PageNodeId != null && _pages.Contains(u.PageNodeId.Value)).ExecuteDeleteAsync();
        await ctx.PageNodeVersions.Where(v => _pages.Contains(v.PageNodeId)).ExecuteDeleteAsync();
        await ctx.PageNodes.IgnoreQueryFilters().Where(n => _pages.Contains(n.Id)).ExecuteDeleteAsync();
        var blockIds = ctx.ContentBlocks.Where(b => _blocks.Contains(b.Key)).Select(b => b.Id);
        await ctx.MediaUsages.Where(u => u.ContentBlockId != null && blockIds.Contains(u.ContentBlockId.Value)).ExecuteDeleteAsync();
        await ctx.ContentBlockVersions.Where(v => blockIds.Contains(v.ContentBlockId)).ExecuteDeleteAsync();
        await ctx.ContentBlocks.Where(b => _blocks.Contains(b.Key)).ExecuteDeleteAsync();
    }

    private static JsonObject Image(Guid id, string crop = "", string aspect = "", string focal = "") => new()
    {
        ["kind"] = "widget",
        ["type"] = "image",
        ["props"] = new JsonObject { ["mediaId"] = id.ToString(), ["alt"] = "a", ["crop"] = crop, ["aspect"] = aspect, ["focal"] = focal },
    };

    private static JsonObject RichText(string html) => new()
    {
        ["kind"] = "widget",
        ["type"] = "richtext",
        ["props"] = new JsonObject { ["html"] = html },
    };

    private static string Tree(params JsonNode[] nodes) => new JsonArray(nodes).ToJsonString();

    private async Task<MediaAssetDto> ImageAsync()
    {
        var id = Guid.NewGuid();
        await using var ctx = fixture.CreateContext();
        var repo = new MediaAssetRepository(ctx, new FakeCurrentUserService());
        var file = new NewMediaAssetVersion(MediaBlobKeys.Original(id, 1), "image/png", 1280, 640, 10, Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"), IsAnimated: false);
        return await repo.CreateAsync(new NewMediaAsset(id, "blocks.png", "blocks", MediaAssetMetadata.Empty, file));
    }

    private async Task<Guid> PageAsync(string title)
    {
        await using var ctx = fixture.CreateContext();
        var node = await new PageNodeService(new PageNodeRepository(ctx))
            .CreatePageAsync(null, $"variant-{Guid.NewGuid():N}", title, "content", "test");
        _pages.Add(node.Id);
        return node.Id;
    }

    private async Task WithPagesAsync(Func<PageNodeService, Task> work)
    {
        await using var ctx = fixture.CreateContext();
        await work(new PageNodeService(new PageNodeRepository(ctx)));
    }

    private Task SaveDraftAsync(Guid page, string content) => WithPagesAsync(s => s.SaveWorkingContentAsync(page, content, "", "test"));

    private Task PublishDraftAsync(Guid page) => WithPagesAsync(s => s.PublishDraftAsync(page, "test"));

    private async Task SaveBlockAsync(string key, string value)
    {
        _blocks.Add(key);
        await using var ctx = fixture.CreateContext();
        await new ContentBlockService(new ContentBlockRepository(ctx), new HtmlRenderingService())
            .SaveAsync(new SaveContentBlockDto { Key = key, BlockType = "Content", Value = value });
    }

    private async Task<T> WithServiceAsync<T>(Func<MediaVariantService, Task<T>> read)
    {
        await using var ctx = fixture.CreateContext();
        return await read(new MediaVariantService(new MediaReportStore(ctx), TimeProvider.System));
    }

    [Fact]
    public async Task Variants_GroupTheCurrentUses_FromRealPageVersionsAndBlocks()
    {
        var image = await ImageAsync();
        var other = await ImageAsync();

        var page = await PageAsync("Image test page");
        await SaveDraftAsync(page, Tree(
            Image(image.Id),
            Image(image.Id, aspect: "16:9", focal: "0.25,0.5"),
            Image(image.Id, crop: "0,0,0.5,1"),
            Image(other.Id, aspect: "1:1")));
        await PublishDraftAsync(page);
        // The draft adds a rich-text use in a square box; the live version stays as it was.
        await SaveDraftAsync(page, Tree(
            Image(image.Id),
            Image(image.Id, aspect: "16:9", focal: "0.25,0.5"),
            Image(image.Id, crop: "0,0,0.5,1"),
            RichText($"<p><img data-media-id=\"{image.Id}\" src=\"/media/{image.Id}/1/blocks.png\" alt=\"b\" width=\"320\" height=\"320\"></p>"),
            Image(other.Id, aspect: "1:1")));

        // An older version used another crop. It is not current, so its crop is not listed.
        var older = await PageAsync("Older crop");
        await SaveDraftAsync(older, Tree(Image(image.Id, aspect: "4:3")));
        await PublishDraftAsync(older);
        await SaveDraftAsync(older, Tree(Image(image.Id)));
        await PublishDraftAsync(older);

        var blockKey = $"variant-{Guid.NewGuid():N}";
        await SaveBlockAsync(blockKey, $"<p><img data-media-id=\"{image.Id}\" src=\"/media/{image.Id}/1/blocks.png\" alt=\"b\"></p>");

        var variants = await WithServiceAsync(s => s.GetVariantsAsync(image));

        Assert.Equal(4, variants.Count);

        var original = variants[0];
        Assert.True(original.Shape.IsOriginal);
        Assert.Equal(["Image test page", "Older crop", blockKey], original.Uses.Select(u => u.Name));
        Assert.Equal([EmbeddedVersionState.Live, EmbeddedVersionState.Draft], original.Uses[0].States);
        Assert.Equal(blockKey, original.Uses[2].Name);
        Assert.NotNull(original.Uses[2].ContentBlockId);

        Assert.Equal("Cropped to 16:9, focal point centre-left", MediaVariantWording.Describe(variants[1].Shape, image.Latest).Title);
        Assert.Equal("Cropped to part of the image", MediaVariantWording.Describe(variants[2].Shape, image.Latest).Title);
        Assert.Equal(page, variants[1].Uses.Single().PageNodeId);

        var box = variants[3];
        Assert.Equal((320, 320), (box.Shape.BoxWidth, box.Shape.BoxHeight));
        var boxUse = box.Uses.Single();
        Assert.Equal(MediaUsageLocations.RichText, boxUse.Location);
        Assert.Equal([EmbeddedVersionState.Draft], boxUse.States);
        Assert.DoesNotContain(variants, v => v.Shape.Cut?.Aspect?.Width == 4);
    }

    [Fact]
    public async Task EveryCurrentPageAndBlockInWhereUsed_IsInAVariant()
    {
        var image = await ImageAsync();
        var page = await PageAsync("Markdown and widgets");
        await SaveDraftAsync(page, $"Some text ![Logo](/media/{image.Id}/1/blocks.png)");
        var other = await PageAsync("Widget page");
        await SaveDraftAsync(other, Tree(Image(image.Id, aspect: "1:1")));
        var blockKey = $"variant-{Guid.NewGuid():N}";
        await SaveBlockAsync(blockKey, $"<img src=\"/media/{image.Id}/1/blocks.png\" alt=\"b\" width=\"100\">");

        IReadOnlyList<MediaUsagePage> pages;
        IReadOnlyList<MediaUsageBlock> blocks;
        await using (var ctx = fixture.CreateContext())
        {
            var repo = new MediaAssetRepository(ctx, new FakeCurrentUserService());
            pages = await repo.GetUsagePagesAsync(image.Id);
            blocks = await repo.GetUsageBlocksAsync(image.Id);
        }
        var variants = await WithServiceAsync(s => s.GetVariantsAsync(image));

        var inVariants = variants.SelectMany(v => v.Uses).ToList();
        Assert.Equal(2, pages.Count(p => p.InCurrentVersion));
        foreach (var p in pages.Where(p => p.InCurrentVersion))
            Assert.Contains(inVariants, u => u.PageNodeId == p.PageNodeId);
        foreach (var b in blocks)
            Assert.Contains(inVariants, u => u.ContentBlockId == b.ContentBlockId);
        Assert.Single(blocks);
    }

    [Fact]
    public async Task Counts_ReadManyImagesAtOnce()
    {
        var a = await ImageAsync();
        var b = await ImageAsync();
        var unused = await ImageAsync();
        var page = await PageAsync("Counts");
        await SaveDraftAsync(page, Tree(Image(a.Id), Image(a.Id, aspect: "1:1"), Image(a.Id, crop: "0,0,0.5,1"), Image(b.Id)));

        var counts = await WithServiceAsync(s => s.CountVariantsAsync([a, b, unused]));

        Assert.Equal((3, 1, 0), (counts[a.Id], counts[b.Id], counts[unused.Id]));
    }
}
