using DfE.CheckPerformanceData.Application.Media;
using DfE.CheckPerformanceData.Application.Media.Folders;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DfE.CheckPerformanceData.IntegrationTests.Media;

// Media library folders against a real Postgres. The database keeps the tree sound on its own:
// names are unique in each place ignoring case (also at the top), a folder cannot be its own
// parent or move inside itself (a trigger, also when two moves race), and a folder that holds an
// image or a folder cannot be deleted. The library list reads a folder's subtree with one
// recursive query.
[Collection(nameof(PostgresCollection))]
public sealed class MediaFolderRepositoryTests(PostgresFixture fixture) : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await using var ctx = fixture.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync("TRUNCATE media_usages, media_asset_versions, media_assets, media_folders");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private MediaFolderRepository Repo(out DfE.CheckPerformanceData.Persistence.Contexts.PortalDbContext ctx)
    {
        ctx = fixture.CreateContext();
        return new MediaFolderRepository(ctx, new FakeCurrentUserService());
    }

    private async Task<Guid> FolderAsync(string name, Guid? parent = null)
    {
        var id = Guid.NewGuid();
        await using var ctx = fixture.CreateContext();
        Assert.Equal(MediaFolderOutcome.Done, await new MediaFolderRepository(ctx, new FakeCurrentUserService()).CreateAsync(id, name, parent));
        return id;
    }

    private async Task<Guid> ImageAsync(Guid? folder, string fileName = "photo.png")
    {
        var id = Guid.NewGuid();
        await using var ctx = fixture.CreateContext();
        await new MediaAssetRepository(ctx, new FakeCurrentUserService()).CreateAsync(new NewMediaAsset(
            id, fileName, "photo", MediaAssetMetadata.Empty,
            new NewMediaAssetVersion(MediaBlobKeys.Original(id, 1), "image/png", 10, 10, 100, Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"), false),
            folder));
        return id;
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task TwoFoldersAtTheTop_MayNotShareANameThatDiffersOnlyInCase()
    {
        await FolderAsync("Photos");

        var sut = Repo(out var ctx);
        await using var _ = ctx;
        Assert.Equal(MediaFolderOutcome.NameTaken, await sut.CreateAsync(Guid.NewGuid(), "photos", null));
    }

    [Fact]
    public async Task TheSameName_IsAllowedInDifferentFolders_ButNotTwiceInOne()
    {
        var a = await FolderAsync("A");
        var b = await FolderAsync("B");
        await FolderAsync("Logos", a);
        await FolderAsync("Logos", b);

        var sut = Repo(out var ctx);
        await using var _ = ctx;
        Assert.Equal(MediaFolderOutcome.NameTaken, await sut.CreateAsync(Guid.NewGuid(), "LOGOS", a));
        // The refused folder is not left in the tracker for the next save.
        Assert.Equal(MediaFolderOutcome.Done, await sut.CreateAsync(Guid.NewGuid(), "Icons", a));
    }

    [Fact]
    public async Task RenamingOntoASiblingsName_IsRefused()
    {
        await FolderAsync("Photos");
        var other = await FolderAsync("Drawings");

        var sut = Repo(out var ctx);
        await using var _ = ctx;
        Assert.Equal(MediaFolderOutcome.NameTaken, await sut.RenameAsync(other, "PHOTOS"));
    }

    [Fact]
    public async Task AFolderInAParentThatDoesNotExist_IsRefused()
    {
        var sut = Repo(out var ctx);
        await using var _ = ctx;

        Assert.Equal(MediaFolderOutcome.ParentNotFound, await sut.CreateAsync(Guid.NewGuid(), "Lost", Guid.NewGuid()));
    }

    [Fact]
    public async Task AFolder_CannotBeItsOwnParent()
    {
        var id = await FolderAsync("Self");

        var ex = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync($"UPDATE media_folders SET parent_id = id WHERE id = '{id}';"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, ex.SqlState);
    }

    // The trigger refuses the loop even when the service is bypassed.
    [Fact]
    public async Task MovingAFolderIntoAFolderInsideIt_IsRefusedByTheDatabase()
    {
        var top = await FolderAsync("Top");
        var middle = await FolderAsync("Middle", top);
        var bottom = await FolderAsync("Bottom", middle);

        var sut = Repo(out var ctx);
        await using (ctx)
            Assert.Equal(MediaFolderOutcome.Cycle, await sut.MoveAsync(top, bottom));

        var ex = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync($"UPDATE media_folders SET parent_id = '{middle}' WHERE id = '{top}';"));
        Assert.Equal("ck_media_folders_no_cycle", ex.ConstraintName);

        await using var read = fixture.CreateContext();
        Assert.Null((await read.MediaFolders.SingleAsync(f => f.Id == top)).ParentId);
    }

    [Fact]
    public async Task MovingAFolderToTheTopOrIntoAnotherBranch_IsAllowed()
    {
        var a = await FolderAsync("A");
        var b = await FolderAsync("B");
        var child = await FolderAsync("Child", a);

        var sut = Repo(out var ctx);
        await using var _ = ctx;
        Assert.Equal(MediaFolderOutcome.Done, await sut.MoveAsync(child, b));
        Assert.Equal(MediaFolderOutcome.Done, await sut.MoveAsync(child, null));
        Assert.Equal(MediaFolderOutcome.Done, await sut.MoveAsync(a, b));
    }

    // Two editors move A into B and B into A at the same time. Each move alone is fine, so only
    // the trigger's lock stops the pair from making a loop: the second move waits, then sees the first.
    [Fact]
    public async Task TwoMovesThatRace_CannotMakeALoop()
    {
        var a = await FolderAsync("A");
        var b = await FolderAsync("B");

        await using var first = new NpgsqlConnection(fixture.ConnectionString);
        await using var second = new NpgsqlConnection(fixture.ConnectionString);
        await first.OpenAsync();
        await second.OpenAsync();
        await using var tx1 = await first.BeginTransactionAsync();
        await using (var move = new NpgsqlCommand($"UPDATE media_folders SET parent_id = '{b}' WHERE id = '{a}';", first, tx1))
            await move.ExecuteNonQueryAsync();

        var racing = Task.Run(async () =>
        {
            await using var move = new NpgsqlCommand($"UPDATE media_folders SET parent_id = '{a}' WHERE id = '{b}';", second);
            await move.ExecuteNonQueryAsync();
        });
        await Task.Delay(500);
        Assert.False(racing.IsCompleted, "The second move should wait for the first.");
        await tx1.CommitAsync();

        var ex = await Assert.ThrowsAsync<PostgresException>(() => racing);
        Assert.Equal("ck_media_folders_no_cycle", ex.ConstraintName);
    }

    [Fact]
    public async Task AFolderThatHoldsAnImageOrAFolder_CannotBeDeleted()
    {
        var withImage = await FolderAsync("With image");
        await ImageAsync(withImage);
        var withFolder = await FolderAsync("With folder");
        await FolderAsync("Inner", withFolder);
        var empty = await FolderAsync("Empty");

        var sut = Repo(out var ctx);
        await using var _ = ctx;
        Assert.Equal(MediaFolderOutcome.NotEmpty, await sut.DeleteAsync(withImage));
        Assert.Equal(MediaFolderOutcome.NotEmpty, await sut.DeleteAsync(withFolder));
        Assert.Equal((1, 0), await sut.CountContentsAsync(withImage));
        Assert.Equal((0, 1), await sut.CountContentsAsync(withFolder));
        Assert.Equal(MediaFolderOutcome.Done, await sut.DeleteAsync(empty));
        Assert.Equal(MediaFolderOutcome.NotFound, await sut.DeleteAsync(empty));
    }

    [Fact]
    public async Task SubtreeIds_FindsEveryFolderInside_AtAnyDepth()
    {
        var top = await FolderAsync("Top");
        var middle = await FolderAsync("Middle", top);
        var bottom = await FolderAsync("Bottom", middle);
        await FolderAsync("Elsewhere");

        var sut = Repo(out var ctx);
        await using var _ = ctx;
        Assert.Equal(new[] { top, middle, bottom }.Order(), (await sut.SubtreeIdsAsync(top)).Order());
        Assert.Equal([bottom], await sut.SubtreeIdsAsync(bottom));
    }

    // With subfolders the list holds the folder's subtree; without, the folder only. At the top,
    // with subfolders is the whole library and without is the images in no folder.
    [Fact]
    public async Task TheLibraryList_ChoosesImagesByFolder_WithOrWithoutSubfolders()
    {
        var top = await FolderAsync("Top");
        var inner = await FolderAsync("Inner", top);
        var other = await FolderAsync("Other");
        var atRoot = await ImageAsync(null);
        var inTop = await ImageAsync(top);
        var inInner = await ImageAsync(inner);
        var inOther = await ImageAsync(other);

        await using var ctx = fixture.CreateContext();
        var assets = new MediaAssetRepository(ctx, new FakeCurrentUserService());
        async Task<IEnumerable<Guid>> Ids(Guid? folder, bool subfolders) =>
            (await assets.ListAsync(new MediaAssetQuery(FolderId: folder, IncludeSubfolders: subfolders))).Items.Select(i => i.Id).Order();

        Assert.Equal(new[] { inTop, inInner }.Order(), await Ids(top, true));
        Assert.Equal([inTop], await Ids(top, false));
        Assert.Equal(new[] { atRoot, inTop, inInner, inOther }.Order(), await Ids(null, true));
        Assert.Equal([atRoot], await Ids(null, false));
        Assert.Equal(top, (await assets.GetAsync(inTop))!.FolderId);
    }

    [Fact]
    public async Task MovingImages_ChangesTheirFolder_AndTheAuditLogRecordsIt()
    {
        var folder = await FolderAsync("Target");
        var one = await ImageAsync(null);
        var two = await ImageAsync(folder);

        var sut = Repo(out var ctx);
        await using (ctx)
        {
            Assert.Equal(1, await sut.MoveImagesAsync([one, two, Guid.NewGuid()], folder));
            Assert.Null(await sut.MoveImagesAsync([one], Guid.NewGuid()));
        }

        await using var read = fixture.CreateContext();
        Assert.Equal(folder, (await read.MediaAssets.SingleAsync(a => a.Id == one)).FolderId);
        var oneText = one.ToString();
        Assert.Contains(await read.AuditEntries.AsNoTracking()
                .Where(a => a.EntityType == nameof(MediaAsset) && a.Action == "Update" && a.EntityId == oneText).ToListAsync(),
            r => r.ChangedColumns!.Contains("FolderId"));
        var folderText = folder.ToString();
        Assert.True(await read.AuditEntries.AnyAsync(a => a.EntityType == nameof(MediaFolder) && a.Action == "Insert" && a.EntityId == folderText));
    }
}
