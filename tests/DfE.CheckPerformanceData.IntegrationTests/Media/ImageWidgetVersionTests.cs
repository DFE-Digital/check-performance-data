using System.Text.Json.Nodes;
using DfE.CheckPerformanceData.Application.ContentPages;
using DfE.CheckPerformanceData.Application.Media;
using DfE.CheckPerformanceData.Application.Media.Serving;
using DfE.CheckPerformanceData.Application.Media.Widget;
using DfE.CheckPerformanceData.Application.PageTree;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.IntegrationTests.Media;

// Which file of a library image a page shows, against a real database. A live or draft page shows
// the image's current version, so replacing the image changes the page. A historic page version
// keeps the version recorded when it was saved, so the version history shows the page as it was.
[Collection(nameof(PostgresCollection))]
public sealed class ImageWidgetVersionTests(PostgresFixture fixture) : IAsyncLifetime
{
    private readonly List<Guid> _pages = [];
    private readonly MediaUrlBuilder _urls = new(new MediaUrlSigner(new FixedKey()));

    public async Task InitializeAsync()
    {
        await using var ctx = fixture.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync("TRUNCATE media_usages, media_asset_versions, media_assets");
    }

    public async Task DisposeAsync()
    {
        await using var ctx = fixture.CreateContext();
        await ctx.MediaUsages.Where(u => u.PageNodeId != null && _pages.Contains(u.PageNodeId.Value)).ExecuteDeleteAsync();
        await ctx.PageNodeVersions.Where(v => _pages.Contains(v.PageNodeId)).ExecuteDeleteAsync();
        await ctx.PageNodes.IgnoreQueryFilters().Where(n => _pages.Contains(n.Id)).ExecuteDeleteAsync();
    }

    private static NewMediaAssetVersion File(Guid id, int version) =>
        new(MediaBlobKeys.Original(id, version), "image/png", 640, 480, 1_000, new string((char)('a' + version), 64), IsAnimated: false);

    private async Task<Guid> ImageAsync()
    {
        var id = Guid.NewGuid();
        await using var ctx = fixture.CreateContext();
        await new MediaAssetRepository(ctx, new FakeCurrentUserService())
            .CreateAsync(new NewMediaAsset(id, "photo.png", "photo", MediaAssetMetadata.Empty, File(id, 1)));
        return id;
    }

    private async Task ReplaceAsync(Guid id, int version)
    {
        await using var ctx = fixture.CreateContext();
        await new MediaAssetRepository(ctx, new FakeCurrentUserService()).AddVersionAsync(id, version, File(id, version));
    }

    private async Task<Guid> PageAsync()
    {
        await using var ctx = fixture.CreateContext();
        var node = await new PageNodeService(new PageNodeRepository(ctx))
            .CreatePageAsync(null, $"image-{Guid.NewGuid():N}", "Image test", "content", "test");
        _pages.Add(node.Id);
        return node.Id;
    }

    private async Task WithServiceAsync(Func<PageNodeService, Task> work)
    {
        await using var ctx = fixture.CreateContext();
        await work(new PageNodeService(new PageNodeRepository(ctx)));
    }

    private static string Tree(Guid image, string caption) =>
        new JsonArray(new JsonObject
        {
            ["kind"] = "widget",
            ["type"] = "image",
            ["props"] = new JsonObject { ["mediaId"] = image.ToString(), ["alt"] = "Photo", ["caption"] = caption }
        }).ToJsonString();

    private async Task<Dictionary<int, Guid>> VersionRowIdsAsync(Guid page)
    {
        await using var ctx = fixture.CreateContext();
        return await ctx.PageNodeVersions.Where(v => v.PageNodeId == page).ToDictionaryAsync(v => v.VersionId, v => v.Id);
    }

    // What the page writes in the img src, through the resolver and the view builder the widget uses.
    private async Task<string> RenderedSrcAsync(Guid image, Guid? pageVersionId)
    {
        await using var ctx = fixture.CreateContext();
        var resolver = new MediaVersionResolver(new MediaAssetRepository(ctx, new FakeCurrentUserService()));
        var media = (await resolver.ResolveAsync([image], pageVersionId))[image];
        var widget = new WidgetNode { Type = "image", Props = new JsonObject { ["mediaId"] = image.ToString(), ["alt"] = "Photo" } };
        return ImageWidgetViewBuilder.Build(widget, media, _urls).Src;
    }

    [Fact]
    public async Task AHistoricPageVersion_ShowsItsRecordedImageVersion_AfterTheImageIsReplaced_WhileTheLivePageShowsTheNewOne()
    {
        var image = await ImageAsync();
        var page = await PageAsync();

        // Version 1 is published with image version 1, then version 2 is published, so version 1
        // is history before the image is replaced.
        await WithServiceAsync(s => s.SaveWorkingContentAsync(page, Tree(image, "first"), "", "test"));
        await WithServiceAsync(s => s.PublishDraftAsync(page, "test"));
        await WithServiceAsync(s => s.SaveWorkingContentAsync(page, Tree(image, "second"), "", "test"));
        await WithServiceAsync(s => s.PublishDraftAsync(page, "test"));
        await ReplaceAsync(image, 2);

        var rows = await VersionRowIdsAsync(page);
        Assert.Contains($"/media/{image:D}/1/", await RenderedSrcAsync(image, rows[1]));
        Assert.Contains($"/media/{image:D}/2/", await RenderedSrcAsync(image, rows[2]));
        Assert.Contains($"/media/{image:D}/2/", await RenderedSrcAsync(image, null));
    }

    [Fact]
    public async Task ADraftPreviewedFromTheVersionHistory_ShowsTheNewFile_AfterTheImageIsReplaced()
    {
        var image = await ImageAsync();
        var page = await PageAsync();
        await WithServiceAsync(s => s.SaveWorkingContentAsync(page, Tree(image, "draft"), "", "test"));

        await ReplaceAsync(image, 2);

        var rows = await VersionRowIdsAsync(page);
        Assert.Contains($"/media/{image:D}/2/", await RenderedSrcAsync(image, rows[1]));
    }

    [Fact]
    public async Task GetManyAsync_ReturnsTheImagesFound_WithTheirCurrentVersion()
    {
        var a = await ImageAsync();
        var b = await ImageAsync();
        await ReplaceAsync(b, 2);

        await using var ctx = fixture.CreateContext();
        var found = await new MediaAssetRepository(ctx, new FakeCurrentUserService())
            .GetManyAsync([a, b, Guid.NewGuid()]);

        Assert.Equal(2, found.Count);
        Assert.Equal(2, found.Single(x => x.Id == b).Latest.Version);
        Assert.Equal(1, found.Single(x => x.Id == a).Latest.Version);
    }

    [Fact]
    public async Task GetPinnedVersionsAsync_ReturnsOnlyTheAskedImages_RecordedForThatPageVersion()
    {
        var a = await ImageAsync();
        var b = await ImageAsync();
        var page = await PageAsync();
        var both = new JsonArray(
            JsonNode.Parse(Tree(a, "a"))!.AsArray()[0]!.DeepClone(),
            JsonNode.Parse(Tree(b, "b"))!.AsArray()[0]!.DeepClone()).ToJsonString();
        await WithServiceAsync(s => s.SaveWorkingContentAsync(page, both, "", "test"));
        var row = (await VersionRowIdsAsync(page))[1];

        await using var ctx = fixture.CreateContext();
        var pinned = await new MediaAssetRepository(ctx, new FakeCurrentUserService()).GetPinnedVersionsAsync(row, [a]);

        var version = Assert.Single(pinned);
        Assert.Equal(a, version.AssetId);
        Assert.Equal(1, version.Version);
    }

    private sealed class FixedKey : IMediaUrlSigningKey
    {
        public byte[] GetKey() => Enumerable.Repeat((byte)3, 32).ToArray();
    }
}
