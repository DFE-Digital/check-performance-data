using System.Text.Json.Nodes;
using DfE.CheckPerformanceData.Application.Media;
using DfE.CheckPerformanceData.Application.PageTree;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.IntegrationTests.Media;

// Usages are written with the page version that holds them, through every path that creates a page
// version or changes its content: save draft, publish, copy, content import and the rebuild. Each
// usage pins the image version that was current when the page version was saved. Replacing an
// image moves the pins of live, scheduled and draft page versions to the new file, and leaves
// older page versions on the file they showed.
[Collection(nameof(PostgresCollection))]
public sealed class MediaUsageTrackingTests(PostgresFixture fixture) : IAsyncLifetime
{
    private readonly List<Guid> _pages = [];

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

    // ── content helpers ──────────────────────────────────────────────────────

    private static JsonObject ImageWidget(Guid id) =>
        new() { ["kind"] = "widget", ["type"] = "image", ["props"] = new JsonObject { ["mediaId"] = id.ToString() } };

    private static JsonObject RichText(params Guid[] ids) =>
        new()
        {
            ["kind"] = "widget",
            ["type"] = "richtext",
            ["props"] = new JsonObject
            {
                ["html"] = "<p>Text</p>" + string.Concat(ids.Select(id => $"<img data-media-id=\"{id}\" src=\"/media/{id}/1/a.png\" alt=\"x\">"))
            }
        };

    private static string Tree(params JsonNode[] nodes) => new JsonArray(nodes).ToJsonString();

    // ── setup helpers ────────────────────────────────────────────────────────

    private static NewMediaAssetVersion File(Guid id, int version) =>
        new(MediaBlobKeys.Original(id, version), "image/png", 640, 480, 1_000, new string((char)('a' + version), 64), IsAnimated: false);

    private async Task<Guid> ImageAsync(int versions = 1)
    {
        var id = Guid.NewGuid();
        await using var ctx = fixture.CreateContext();
        var repo = new MediaAssetRepository(ctx, new FakeCurrentUserService());
        await repo.CreateAsync(new NewMediaAsset(id, "photo.png", "photo", MediaAssetMetadata.Empty, File(id, 1)));
        for (var v = 2; v <= versions; v++) await repo.AddVersionAsync(id, v, File(id, v));
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
            .CreatePageAsync(null, $"usage-{Guid.NewGuid():N}", "Usage test", "content", "test");
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

    private sealed record Row(Guid AssetId, int AssetVersion, int PageVersion, string Location, string? WidgetId);

    private async Task<List<Row>> UsagesAsync(Guid page)
    {
        await using var ctx = fixture.CreateContext();
        return await (from u in ctx.MediaUsages
                      join v in ctx.PageNodeVersions on u.PageNodeVersionId equals v.Id
                      where u.PageNodeId == page
                      orderby v.VersionId, u.WidgetId, u.Location, u.AssetId
                      select new Row(u.AssetId, u.AssetVersion, v.VersionId, u.Location, u.WidgetId))
            .ToListAsync();
    }

    // ── tests ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SavingADraft_WritesAUsageForEachImage_PinnedToItsCurrentVersion_AndIgnoresUnknownIds()
    {
        var a = await ImageAsync(versions: 2);
        var b = await ImageAsync();
        var page = await PageAsync();

        await SaveDraftAsync(page, Tree(ImageWidget(a), RichText(b, Guid.NewGuid()), ImageWidget(Guid.NewGuid())));

        Assert.Equal(
            [
                new Row(a, 2, 1, MediaUsageLocations.Widget, "0.0"),
                new Row(b, 1, 1, MediaUsageLocations.RichText, "0.1"),
            ],
            await UsagesAsync(page));
    }

    [Fact]
    public async Task Publishing_KeepsTheUsages()
    {
        var a = await ImageAsync();
        var page = await PageAsync();
        await SaveDraftAsync(page, Tree(ImageWidget(a)));

        await PublishDraftAsync(page);

        Assert.Equal([new Row(a, 1, 1, MediaUsageLocations.Widget, "0.0")], await UsagesAsync(page));
    }

    // The draft is edited in place: its rows are replaced and pinned to the then-current versions.
    [Fact]
    public async Task EditingADraft_ReplacesItsUsages()
    {
        var a = await ImageAsync();
        var b = await ImageAsync();
        var page = await PageAsync();
        await SaveDraftAsync(page, Tree(ImageWidget(a), RichText(b)));

        await SaveDraftAsync(page, Tree(RichText(b), ImageWidget(b)));

        Assert.Equal(
            [
                new Row(b, 1, 1, MediaUsageLocations.RichText, "0.0"),
                new Row(b, 1, 1, MediaUsageLocations.Widget, "0.1"),
            ],
            await UsagesAsync(page));
    }

    // After a publish the next save makes a new version. The published version keeps its rows.
    [Fact]
    public async Task ANewVersion_GetsItsOwnUsages_AndThePublishedVersionKeepsItsRows()
    {
        var a = await ImageAsync();
        var b = await ImageAsync();
        var page = await PageAsync();
        await SaveDraftAsync(page, Tree(ImageWidget(a)));
        await PublishDraftAsync(page);

        await SaveDraftAsync(page, Tree(ImageWidget(b)));

        Assert.Equal(
            [
                new Row(a, 1, 1, MediaUsageLocations.Widget, "0.0"),
                new Row(b, 1, 2, MediaUsageLocations.Widget, "0.0"),
            ],
            await UsagesAsync(page));
    }

    // Deleting a page keeps its versions, so their usages stay; restoring brings the page back with them.
    [Fact]
    public async Task DeletingAndRestoringAPage_KeepsItsUsages()
    {
        var a = await ImageAsync();
        var page = await PageAsync();
        await SaveDraftAsync(page, Tree(ImageWidget(a)));

        await WithServiceAsync(s => s.DeleteAsync(page, "test"));
        Assert.Single(await UsagesAsync(page));

        await WithServiceAsync(s => s.RestoreAsync(page, "test"));
        Assert.Equal([new Row(a, 1, 1, MediaUsageLocations.Widget, "0.0")], await UsagesAsync(page));
    }

    // Content import replaces every version with new ids, so the usages are rebuilt from the content.
    [Fact]
    public async Task Import_RebuildsTheUsages_ForTheNewVersions()
    {
        var a = await ImageAsync();
        var b = await ImageAsync(versions: 3);
        var page = await PageAsync();
        await SaveDraftAsync(page, Tree(ImageWidget(a)));

        await using (var ctx = fixture.CreateContext())
        {
            await new PageNodeRepository(ctx).ReplaceAllVersionsForStagingAsync(page,
            [
                new PageNodeVersionDto { VersionId = 1, Content = Tree(RichText(a)), PublishFrom = DateTime.UtcNow.AddDays(-2) },
                new PageNodeVersionDto { VersionId = 2, Content = Tree(ImageWidget(b), RichText(Guid.NewGuid())) },
            ], "test");
        }

        Assert.Equal(
            [
                new Row(a, 1, 1, MediaUsageLocations.RichText, "0.0"),
                new Row(b, 3, 2, MediaUsageLocations.Widget, "0.0"),
            ],
            await UsagesAsync(page));
        await using var read = fixture.CreateContext();
        var versionIds = await read.PageNodeVersions.Where(v => v.PageNodeId == page).Select(v => v.Id).ToListAsync();
        Assert.All(await read.MediaUsages.Where(u => u.PageNodeId == page).ToListAsync(),
            u => Assert.Contains(u.PageNodeVersionId!.Value, versionIds));
    }

    // A copy has the same history as its source, so its older versions keep the source's pins.
    [Fact]
    public async Task CopyingAPage_CopiesTheUsages_WithTheirPins()
    {
        var a = await ImageAsync();
        var page = await PageAsync();
        await SaveDraftAsync(page, Tree(ImageWidget(a)));
        await PublishDraftAsync(page);
        await SaveDraftAsync(page, Tree(RichText(a)));
        await PublishDraftAsync(page);                  // v1 is an older version, pinned to 1
        await ReplaceAsync(a, 2);                       // v2 is live, pinned to 2

        Guid copy = Guid.Empty;
        await WithServiceAsync(async s => copy = (await s.CopyPageAsync(page, "test"))!.Id);
        _pages.Add(copy);

        Assert.Equal(
            [
                new Row(a, 1, 1, MediaUsageLocations.Widget, "0.0"),
                new Row(a, 2, 2, MediaUsageLocations.RichText, "0.0"),
            ],
            await UsagesAsync(copy));
    }

    // v1 was live and is now replaced by v2 (live), v3 is scheduled, v4 is the draft. Only v1 is
    // an older version, so only its row keeps the old file.
    [Fact]
    public async Task ReplacingAnImage_RepinsLiveScheduledAndDraftVersions_AndLeavesOlderVersions()
    {
        var a = await ImageAsync();
        var page = await PageAsync();
        await SaveDraftAsync(page, Tree(ImageWidget(a)));
        await PublishDraftAsync(page);                                          // v1 live
        await SaveDraftAsync(page, Tree(ImageWidget(a), RichText(a)));
        await PublishDraftAsync(page);                                          // v2 live, v1 older
        await SaveDraftAsync(page, Tree(RichText(a)));
        await WithServiceAsync(s => s.PublishAsync(page, 3, DateTime.UtcNow.AddDays(7), null, "test"));  // v3 scheduled
        await SaveDraftAsync(page, Tree(ImageWidget(a)));                        // v4 draft

        await ReplaceAsync(a, 2);

        Assert.Equal(
            [
                new Row(a, 1, 1, MediaUsageLocations.Widget, "0.0"),
                new Row(a, 2, 2, MediaUsageLocations.Widget, "0.0"),
                new Row(a, 2, 2, MediaUsageLocations.RichText, "0.1"),
                new Row(a, 2, 3, MediaUsageLocations.RichText, "0.0"),
                new Row(a, 2, 4, MediaUsageLocations.Widget, "0.0"),
            ],
            await UsagesAsync(page));
    }

    // An older draft (unpublished, with a newer draft above it) is an older version too.
    [Fact]
    public async Task ReplacingAnImage_LeavesAnUnpublishedOlderVersion()
    {
        var a = await ImageAsync();
        var page = await PageAsync();
        await SaveDraftAsync(page, Tree(ImageWidget(a)));
        await PublishDraftAsync(page);
        await SaveDraftAsync(page, Tree(RichText(a)));                           // v2 draft
        await WithServiceAsync(s => s.UnpublishAsync(page, "test"));             // v1 back to a draft, below v2

        await ReplaceAsync(a, 2);

        Assert.Equal(
            [
                new Row(a, 1, 1, MediaUsageLocations.Widget, "0.0"),
                new Row(a, 2, 2, MediaUsageLocations.RichText, "0.0"),
            ],
            await UsagesAsync(page));
    }

    [Fact]
    public async Task RemovingAPageForGood_RemovesItsUsages()
    {
        var a = await ImageAsync();
        var page = await PageAsync();
        await SaveDraftAsync(page, Tree(ImageWidget(a)));
        Assert.NotEmpty(await UsagesAsync(page));

        await using (var ctx = fixture.CreateContext())
            await ctx.PageNodes.IgnoreQueryFilters().Where(n => n.Id == page).ExecuteDeleteAsync();

        await using var read = fixture.CreateContext();
        Assert.False(await read.MediaUsages.AnyAsync(u => u.PageNodeId == page));
    }

    // Repair: missing rows come back pinned to the current file, rows for images no longer in the
    // content go, existing rows keep their pins, and a second run changes nothing.
    [Fact]
    public async Task Rebuild_RestoresMissingRows_DropsStaleOnes_KeepsPins_AndIsIdempotent()
    {
        var a = await ImageAsync();
        var b = await ImageAsync();
        var page = await PageAsync();
        await SaveDraftAsync(page, Tree(ImageWidget(a)));
        await PublishDraftAsync(page);
        await SaveDraftAsync(page, Tree(RichText(b)));
        await ReplaceAsync(a, 2);                       // v1 is live: its pin moves to 2
        await ReplaceAsync(b, 2);
        await ReplaceAsync(b, 3);
        var expected = await UsagesAsync(page);

        await using (var ctx = fixture.CreateContext())
        {
            // Break it: drop v2's row and add a stale row to v1.
            var v1 = await ctx.PageNodeVersions.Where(v => v.PageNodeId == page && v.VersionId == 1).Select(v => v.Id).SingleAsync();
            await ctx.MediaUsages.Where(u => u.PageNodeId == page && u.AssetId == b).ExecuteDeleteAsync();
            ctx.MediaUsages.Add(new MediaUsage
            {
                Id = Guid.NewGuid(), AssetId = b, AssetVersion = 1, PageNodeId = page, PageNodeVersionId = v1,
                Location = MediaUsageLocations.RichText, WidgetId = "0.5"
            });
            await ctx.SaveChangesAsync();
        }

        int changed;
        await using (var ctx = fixture.CreateContext())
            changed = await new PageNodeRepository(ctx).RebuildMediaUsagesAsync();

        Assert.Equal(2, changed);
        Assert.Equal(expected, await UsagesAsync(page));

        await using (var ctx = fixture.CreateContext())
            Assert.Equal(0, await new PageNodeRepository(ctx).RebuildMediaUsagesAsync());
        Assert.Equal(expected, await UsagesAsync(page));
    }

    // A rebuild keeps the pin of an older version: it is history, not something to repair.
    [Fact]
    public async Task Rebuild_KeepsTheOldPin_OfAnOlderVersion()
    {
        var a = await ImageAsync();
        var page = await PageAsync();
        await SaveDraftAsync(page, Tree(ImageWidget(a)));
        await PublishDraftAsync(page);
        await SaveDraftAsync(page, Tree(RichText(a)));
        await PublishDraftAsync(page);
        await ReplaceAsync(a, 2);

        await using (var ctx = fixture.CreateContext())
            Assert.Equal(0, await new PageNodeRepository(ctx).RebuildMediaUsagesAsync());

        Assert.Equal(
            [
                new Row(a, 1, 1, MediaUsageLocations.Widget, "0.0"),
                new Row(a, 2, 2, MediaUsageLocations.RichText, "0.0"),
            ],
            await UsagesAsync(page));
    }

    // Where-used: pages whose live, scheduled or draft version uses the image, and pages where only
    // an older version does.
    [Fact]
    public async Task GetUsagePages_TellsCurrentUseFromOlderVersions()
    {
        var a = await ImageAsync();
        var current = await PageAsync();
        await SaveDraftAsync(current, Tree(ImageWidget(a)));
        var older = await PageAsync();
        await SaveDraftAsync(older, Tree(ImageWidget(a)));
        await PublishDraftAsync(older);
        await SaveDraftAsync(older, Tree(RichText()));
        await PublishDraftAsync(older);

        await using var ctx = fixture.CreateContext();
        var pages = await new MediaAssetRepository(ctx, new FakeCurrentUserService()).GetUsagePagesAsync(a);

        Assert.True(pages.Single(p => p.PageNodeId == current).InCurrentVersion);
        Assert.False(pages.Single(p => p.PageNodeId == older).InCurrentVersion);
    }
}
