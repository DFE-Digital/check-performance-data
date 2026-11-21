using DfE.CheckPerformanceData.Application.Common;
using DfE.CheckPerformanceData.Application.ContentBlocks;
using DfE.CheckPerformanceData.Application.Media;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DfE.CheckPerformanceData.IntegrationTests.Media;

// A content block records the library images its current value uses, in the same save as the
// value: a new block, an edit, a revert and a content import. The rows go when the block goes.
// The library counts them: the delete guard refuses the image, the image page lists the block,
// and replacing the image moves the block to the new file.
[Collection(nameof(PostgresCollection))]
public sealed class ContentBlockMediaUsageTests(PostgresFixture fixture) : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await using var ctx = fixture.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync(
            @"TRUNCATE media_usages, media_asset_versions, media_assets, ""ContentBlocks"", ""ContentBlockVersions"" RESTART IDENTITY CASCADE");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static string Img(Guid id) => $"<img data-media-id=\"{id}\" src=\"/media/{id}/a.png\" alt=\"x\">";

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

    private static ContentBlockService Service(PortalDbContext ctx) =>
        new(new ContentBlockRepository(ctx), new HtmlRenderingService());

    private async Task<ContentBlockDto> SaveAsync(string key, string value, string type = "Content")
    {
        await using var ctx = fixture.CreateContext();
        return await Service(ctx).SaveAsync(new SaveContentBlockDto { Key = key, BlockType = type, Value = value });
    }

    private sealed record Row(Guid AssetId, int AssetVersion, string Location);

    private async Task<List<Row>> UsagesAsync(int blockId)
    {
        await using var ctx = fixture.CreateContext();
        return await ctx.MediaUsages
            .Where(u => u.ContentBlockId == blockId)
            .OrderBy(u => u.AssetId)
            .Select(u => new Row(u.AssetId, u.AssetVersion, u.Location))
            .ToListAsync();
    }

    [Fact]
    public async Task SavingANewBlock_WritesAUsageForEachLibraryImage_AndIgnoresUnknownIds()
    {
        var a = await ImageAsync();

        var block = await SaveAsync("usage-new", "<p>Text</p>" + Img(a) + Img(Guid.NewGuid()));

        Assert.Equal([new Row(a, 1, MediaUsageLocations.RichText)], await UsagesAsync(block.Id));
    }

    [Fact]
    public async Task EnsuringABlock_WritesTheUsagesOfItsDefault()
    {
        var a = await ImageAsync();
        await using var ctx = fixture.CreateContext();

        var block = await Service(ctx).EnsureAsync("usage-ensure", "Content", Img(a), "/somewhere");

        Assert.Equal([new Row(a, 1, MediaUsageLocations.RichText)], await UsagesAsync(block.Id));
    }

    [Fact]
    public async Task EditingABlock_ReplacesItsUsages()
    {
        var a = await ImageAsync();
        var b = await ImageAsync();
        var block = await SaveAsync("usage-edit", Img(a));

        await SaveAsync("usage-edit", Img(b) + Img(b));

        Assert.Equal([new Row(b, 1, MediaUsageLocations.RichText)], await UsagesAsync(block.Id));
    }

    [Fact]
    public async Task RevertingABlock_WritesTheUsagesOfTheOldValue()
    {
        var a = await ImageAsync();
        var block = await SaveAsync("usage-revert", Img(a));
        await SaveAsync("usage-revert", "<p>No image</p>");
        Assert.Empty(await UsagesAsync(block.Id));

        await using (var ctx = fixture.CreateContext())
        {
            var service = Service(ctx);
            var first = (await service.GetVersionsAsync("usage-revert")).Single(v => v.VersionNumber == 1);
            await service.RevertToVersionAsync("usage-revert", first.Id);
        }

        Assert.Equal([new Row(a, 1, MediaUsageLocations.RichText)], await UsagesAsync(block.Id));
    }

    // Content import replaces a block in place, and may change its type. A text block shows its
    // value as text, so it uses no image.
    [Fact]
    public async Task ImportingOverABlock_ReplacesItsUsages_AndATextBlockHasNone()
    {
        var a = await ImageAsync();
        var b = await ImageAsync();
        var block = await SaveAsync("usage-import", Img(a));

        await using (var ctx = fixture.CreateContext())
        {
            var repo = new ContentBlockRepository(ctx);
            await repo.UpdateForStagingAsync(block.Id, block.Key, "Content", Img(b), "", block.ContentId, true, null);
            await repo.SaveChangesAsync();
        }
        Assert.Equal([new Row(b, 1, MediaUsageLocations.RichText)], await UsagesAsync(block.Id));

        await using (var ctx = fixture.CreateContext())
        {
            var repo = new ContentBlockRepository(ctx);
            await repo.UpdateForStagingAsync(block.Id, block.Key, "Title", Img(b), "", block.ContentId, true, null);
            await repo.SaveChangesAsync();
        }
        Assert.Empty(await UsagesAsync(block.Id));
    }

    [Fact]
    public async Task ImportingANewBlock_WritesItsUsages_InTheSameSave()
    {
        var a = await ImageAsync();
        await using var ctx = fixture.CreateContext();

        var block = await new ContentBlockRepository(ctx).AddBlockAsync("usage-import-new", "Content", Img(a), "", Guid.NewGuid());

        Assert.Equal([new Row(a, 1, MediaUsageLocations.RichText)], await UsagesAsync(block.Id));
    }

    [Fact]
    public async Task RemovingABlock_RemovesItsUsages()
    {
        var a = await ImageAsync();
        var block = await SaveAsync("usage-remove", Img(a));
        Assert.NotEmpty(await UsagesAsync(block.Id));

        await using (var ctx = fixture.CreateContext())
            await ctx.ContentBlocks.Where(b => b.Id == block.Id).ExecuteDeleteAsync();

        await using var verify = fixture.CreateContext();
        Assert.False(await verify.MediaUsages.AnyAsync(u => u.AssetId == a));
    }

    // A usage belongs to a page version or to a content block, never both and never neither.
    [Fact]
    public async Task AUsage_NeedsExactlyOneOwner()
    {
        var a = await ImageAsync();
        var block = await SaveAsync("usage-owner", "<p>Text</p>");

        await using var ctx = fixture.CreateContext();
        var ex = await Assert.ThrowsAsync<PostgresException>(() => ctx.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO media_usages (id, asset_id, asset_version, location) VALUES ({Guid.NewGuid()}, {a}, 1, 'richtext')"));
        Assert.Equal("ck_media_usages_owner", ex.ConstraintName);

        // The check runs before the foreign keys, so made-up page ids are enough to reach it.
        ex = await Assert.ThrowsAsync<PostgresException>(() => ctx.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO media_usages (id, asset_id, asset_version, location, page_node_id, page_node_version_id, content_block_id) VALUES ({Guid.NewGuid()}, {a}, 1, 'richtext', {Guid.NewGuid()}, {Guid.NewGuid()}, {block.Id})"));
        Assert.Equal("ck_media_usages_owner", ex.ConstraintName);
    }

    [Fact]
    public async Task AnImageUsedOnlyInABlock_CannotBeDeleted()
    {
        var a = await ImageAsync();
        await SaveAsync("usage-guard", Img(a));

        await using var ctx = fixture.CreateContext();
        var outcome = await new MediaAssetRepository(ctx, new FakeCurrentUserService()).DeleteAsync(a);

        Assert.Equal(MediaDeleteOutcome.InUse, outcome);
    }

    [Fact]
    public async Task GetUsageBlocks_ListsTheBlocksThatUseTheImage_ByKey()
    {
        var a = await ImageAsync();
        var second = await SaveAsync("usage-where-b", Img(a));
        var first = await SaveAsync("usage-where-a", "<p>Intro</p>" + Img(a));
        await SaveAsync("usage-where-c", "<p>No image</p>");

        await using var ctx = fixture.CreateContext();
        var blocks = await new MediaAssetRepository(ctx, new FakeCurrentUserService()).GetUsageBlocksAsync(a);

        Assert.Equal(
            [new MediaUsageBlock(first.Id, "usage-where-a"), new MediaUsageBlock(second.Id, "usage-where-b")],
            blocks);
    }

    // A block always shows the newest file, so replacing the image moves its usage too.
    [Fact]
    public async Task ReplacingAnImage_RepinsTheBlocksThatUseIt()
    {
        var a = await ImageAsync();
        var block = await SaveAsync("usage-replace", Img(a));

        await using (var ctx = fixture.CreateContext())
            await new MediaAssetRepository(ctx, new FakeCurrentUserService()).AddVersionAsync(a, 2, File(a, 2));

        Assert.Equal([new Row(a, 2, MediaUsageLocations.RichText)], await UsagesAsync(block.Id));
    }
}
