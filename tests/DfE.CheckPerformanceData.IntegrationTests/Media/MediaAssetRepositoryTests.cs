using DfE.CheckPerformanceData.Application.Media;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DfE.CheckPerformanceData.IntegrationTests.Media;

// The media library's records against a real Postgres: create with a first version, replace,
// metadata edits, list filters and paging, duplicate lookup by SHA-256, the database guards that
// keep versions immutable and used versions undeletable, and which rows the audit log captures.
[Collection(nameof(PostgresCollection))]
public sealed class MediaAssetRepositoryTests(PostgresFixture fixture) : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await using var ctx = fixture.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync("TRUNCATE media_usages, media_asset_versions, media_assets");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static string Sha(char c) => new(c, 64);

    private static NewMediaAssetVersion File(Guid id, int version, char sha = 'a', string type = "image/png") =>
        new(MediaBlobKeys.Original(id, version), type, 640, 480, 12_345, Sha(sha), IsAnimated: false);

    private static NewMediaAsset Asset(Guid id, string fileName = "photo.png", MediaAssetMetadata? metadata = null, char sha = 'a') =>
        new(id, fileName, Path.GetFileNameWithoutExtension(fileName), metadata ?? MediaAssetMetadata.Empty, File(id, 1, sha));

    [Fact]
    public async Task Create_StoresTheImageAndItsFirstVersion()
    {
        var id = Guid.NewGuid();
        await using var ctx = fixture.CreateContext();
        var sut = new MediaAssetRepository(ctx, new FakeCurrentUserService());

        var created = await sut.CreateAsync(Asset(id, "School photo.png",
            new MediaAssetMetadata("Pupils in a classroom", "Year 6", "DfE", ["schools", "key stage 2"])));

        Assert.Equal(1, created.CurrentVersion);
        await using var read = fixture.CreateContext();
        var got = await new MediaAssetRepository(read, new FakeCurrentUserService()).GetAsync(id);
        Assert.NotNull(got);
        Assert.Equal("School photo.png", got!.FileName);
        Assert.Equal("Pupils in a classroom", got.DefaultAltText);
        Assert.Equal(["schools", "key stage 2"], got.Tags);
        Assert.Equal("test-user@education.gov.uk", got.CreatedBy);
        Assert.Equal(1, got.Latest.Version);
        Assert.Equal($"originals/{id:D}/1", got.Latest.BlobKey);
        Assert.Equal("image/png", got.Latest.ContentType);
        Assert.Equal(640, got.Latest.Width);
        Assert.Equal(480, got.Latest.Height);
        Assert.Equal(12_345, got.Latest.SizeBytes);
        Assert.Equal(Sha('a'), got.Latest.Sha256);
        Assert.Equal("test-user@education.gov.uk", got.Latest.UploadedBy);
    }

    [Fact]
    public async Task Get_ReturnsNull_ForAnUnknownImage()
    {
        await using var ctx = fixture.CreateContext();
        Assert.Null(await new MediaAssetRepository(ctx, new FakeCurrentUserService()).GetAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Create_RefusesAnIdThatIsAlreadyTaken()
    {
        var id = Guid.NewGuid();
        await using (var ctx = fixture.CreateContext())
            await new MediaAssetRepository(ctx, new FakeCurrentUserService()).CreateAsync(Asset(id));

        await using var again = fixture.CreateContext();
        await Assert.ThrowsAsync<DbUpdateException>(() =>
            new MediaAssetRepository(again, new FakeCurrentUserService()).CreateAsync(Asset(id)));
    }

    [Fact]
    public async Task AddVersion_MakesTheNewFileCurrent_AndKeepsTheOldOne()
    {
        var id = Guid.NewGuid();
        await using (var ctx = fixture.CreateContext())
            await new MediaAssetRepository(ctx, new FakeCurrentUserService()).CreateAsync(Asset(id));

        await using (var ctx = fixture.CreateContext())
        {
            var added = await new MediaAssetRepository(ctx, new FakeCurrentUserService())
                .AddVersionAsync(id, 2, File(id, 2, 'b', "image/webp"));
            Assert.Equal(2, added.Version);
        }

        await using var read = fixture.CreateContext();
        var sut = new MediaAssetRepository(read, new FakeCurrentUserService());
        var got = await sut.GetAsync(id);
        Assert.Equal(2, got!.CurrentVersion);
        Assert.Equal("image/webp", got.Latest.ContentType);
        Assert.Equal(Sha('a'), (await sut.GetVersionAsync(id, 1))!.Sha256);
        Assert.Equal([2, 1], (await sut.GetVersionsAsync(id)).Select(v => v.Version));
        Assert.Null(await sut.GetVersionAsync(id, 3));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task AddVersion_RefusesANumberThatIsNotTheNextOne(int version)
    {
        var id = Guid.NewGuid();
        await using (var ctx = fixture.CreateContext())
            await new MediaAssetRepository(ctx, new FakeCurrentUserService()).CreateAsync(Asset(id));

        await using var again = fixture.CreateContext();
        var ex = await Assert.ThrowsAsync<MediaVersionConflictException>(() =>
            new MediaAssetRepository(again, new FakeCurrentUserService()).AddVersionAsync(id, version, File(id, version, 'b')));
        Assert.Equal(id, ex.AssetId);

        await using var read = fixture.CreateContext();
        Assert.Equal(1, (await new MediaAssetRepository(read, new FakeCurrentUserService()).GetAsync(id))!.CurrentVersion);
    }

    // Two replaces read the same current version and both try to add version 2. The first wins; the
    // second must fail as a conflict, not overwrite the winner's row.
    [Fact]
    public async Task AddVersion_RacingReplaces_OnlyOneWins()
    {
        var id = Guid.NewGuid();
        await using (var ctx = fixture.CreateContext())
            await new MediaAssetRepository(ctx, new FakeCurrentUserService()).CreateAsync(Asset(id));

        await using var first = fixture.CreateContext();
        await using var second = fixture.CreateContext();
        // Both contexts load the image before either saves.
        await first.MediaAssets.SingleAsync(a => a.Id == id);
        await second.MediaAssets.SingleAsync(a => a.Id == id);

        await new MediaAssetRepository(first, new FakeCurrentUserService()).AddVersionAsync(id, 2, File(id, 2, 'b'));
        await Assert.ThrowsAsync<MediaVersionConflictException>(() =>
            new MediaAssetRepository(second, new FakeCurrentUserService()).AddVersionAsync(id, 2, File(id, 2, 'c')));
        // The losing context stays usable, and a later save writes nothing for the failed replace.
        await second.SaveChangesAsync();

        await using var read = fixture.CreateContext();
        Assert.Equal(Sha('b'), (await new MediaAssetRepository(read, new FakeCurrentUserService()).GetVersionAsync(id, 2))!.Sha256);
        Assert.Equal(1, await read.AuditEntries.CountAsync(a =>
            a.EntityType == nameof(MediaAssetVersion) && a.EntityId == $"{id},2" && a.Action == "Insert"));
        Assert.Equal(1, await read.AuditEntries.CountAsync(a =>
            a.EntityType == nameof(MediaAsset) && a.EntityId == id.ToString() && a.Action == "Update"));
    }

    [Fact]
    public async Task AddVersion_ThrowsForAnUnknownImage()
    {
        var id = Guid.NewGuid();
        await using var ctx = fixture.CreateContext();
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            new MediaAssetRepository(ctx, new FakeCurrentUserService()).AddVersionAsync(id, 2, File(id, 2)));
    }

    [Fact]
    public async Task UpdateMetadata_ReplacesTheDescription_AndLeavesTheFileAlone()
    {
        var id = Guid.NewGuid();
        await using (var ctx = fixture.CreateContext())
            await new MediaAssetRepository(ctx, new FakeCurrentUserService())
                .CreateAsync(Asset(id, metadata: new MediaAssetMetadata("Old", "Old caption", null, ["a"])));

        await using (var ctx = fixture.CreateContext())
        {
            Assert.True(await new MediaAssetRepository(ctx, new FakeCurrentUserService())
                .UpdateMetadataAsync(id, new MediaAssetMetadata("New alt", null, "Crown copyright", ["b", "c"])));
        }

        await using var read = fixture.CreateContext();
        var got = await new MediaAssetRepository(read, new FakeCurrentUserService()).GetAsync(id);
        Assert.Equal("New alt", got!.DefaultAltText);
        Assert.Null(got.Caption);
        Assert.Equal("Crown copyright", got.Credit);
        Assert.Equal(["b", "c"], got.Tags);
        Assert.Equal(1, got.CurrentVersion);
    }

    [Fact]
    public async Task UpdateMetadata_ReturnsFalse_ForAnUnknownImage()
    {
        await using var ctx = fixture.CreateContext();
        Assert.False(await new MediaAssetRepository(ctx, new FakeCurrentUserService())
            .UpdateMetadataAsync(Guid.NewGuid(), MediaAssetMetadata.Empty));
    }

    [Fact]
    public async Task FindBySha256_FindsCurrentAndEarlierVersions_Once()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await using (var ctx = fixture.CreateContext())
        {
            var sut = new MediaAssetRepository(ctx, new FakeCurrentUserService());
            await sut.CreateAsync(Asset(a, sha: 'd'));
            await sut.AddVersionAsync(a, 2, File(a, 2, 'd'));
            await sut.CreateAsync(Asset(b, sha: 'e'));
            await sut.AddVersionAsync(b, 2, File(b, 2, 'f'));
        }

        await using var read = fixture.CreateContext();
        var repo = new MediaAssetRepository(read, new FakeCurrentUserService());
        Assert.Equal([a], (await repo.FindBySha256Async(Sha('d'))).Select(x => x.Id));
        Assert.Equal([b], (await repo.FindBySha256Async(Sha('e'))).Select(x => x.Id));
        Assert.Equal([b], (await repo.FindBySha256Async(Sha('F'))).Select(x => x.Id));
        Assert.Empty(await repo.FindBySha256Async(Sha('9')));
    }

    [Fact]
    public async Task List_PagesNewestChangeFirst_AndCountsEverything()
    {
        var ids = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToList();
        foreach (var id in ids)
        {
            await using var ctx = fixture.CreateContext();
            await new MediaAssetRepository(ctx, new FakeCurrentUserService()).CreateAsync(Asset(id));
        }

        await using var read = fixture.CreateContext();
        var sut = new MediaAssetRepository(read, new FakeCurrentUserService());
        var first = await sut.ListAsync(new MediaAssetQuery(PageSize: 2));
        var last = await sut.ListAsync(new MediaAssetQuery(Page: 99, PageSize: 2));

        Assert.Equal(5, first.TotalCount);
        Assert.Equal(3, first.TotalPages);
        Assert.Equal([ids[4], ids[3]], first.Items.Select(i => i.Id));
        Assert.Equal(3, last.Page);
        Assert.Equal([ids[0]], last.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task List_Filters_BySearchTagAndMissingAltText()
    {
        var chart = Guid.NewGuid();
        var pupils = Guid.NewGuid();
        var logo = Guid.NewGuid();
        await using (var ctx = fixture.CreateContext())
        {
            var sut = new MediaAssetRepository(ctx, new FakeCurrentUserService());
            await sut.CreateAsync(Asset(chart, "results-chart.png", new MediaAssetMetadata("Bar chart of results", null, null, ["Charts"])));
            await sut.CreateAsync(Asset(pupils, "pupils.jpg", new MediaAssetMetadata(null, "Pupils at 100% attendance", null, ["photos", "key stage 2"])));
            await sut.CreateAsync(Asset(logo, "logo.svg", new MediaAssetMetadata("   ", null, "DfE design", [])));
        }

        await using var read = fixture.CreateContext();
        var repo = new MediaAssetRepository(read, new FakeCurrentUserService());
        async Task<IEnumerable<Guid>> Ids(MediaAssetQuery q) => (await repo.ListAsync(q)).Items.Select(i => i.Id).Order();

        Assert.Equal([chart], await Ids(new MediaAssetQuery(Search: "CHART")));
        Assert.Equal([pupils], await Ids(new MediaAssetQuery(Search: "100%")));
        // A wildcard character matches itself, not everything.
        Assert.Equal([pupils], await Ids(new MediaAssetQuery(Search: "%")));
        Assert.Equal([logo], await Ids(new MediaAssetQuery(Search: "design")));
        Assert.Equal([pupils], await Ids(new MediaAssetQuery(Search: "stage 2")));
        Assert.Equal([chart], await Ids(new MediaAssetQuery(Tag: "charts")));
        Assert.Equal([pupils], await Ids(new MediaAssetQuery(Tag: "key stage 2")));
        Assert.Empty(await Ids(new MediaAssetQuery(Tag: "key")));
        Assert.Equal(new[] { pupils, logo }.Order(), await Ids(new MediaAssetQuery(MissingAltText: true)));
    }

    [Fact]
    public async Task List_SortedByName_IgnoresCase_AndBreaksTiesById()
    {
        var zebra = Guid.NewGuid();
        var apple = Guid.NewGuid();
        var twinA = new Guid("00000000-0000-0000-0000-000000000001");
        var twinB = new Guid("00000000-0000-0000-0000-000000000002");
        foreach (var (id, name) in new[] { (zebra, "zebra.png"), (twinB, "Middle.png"), (apple, "Apple.png"), (twinA, "middle.png") })
        {
            await using var ctx = fixture.CreateContext();
            await new MediaAssetRepository(ctx, new FakeCurrentUserService()).CreateAsync(Asset(id, name));
        }

        await using var read = fixture.CreateContext();
        var sut = new MediaAssetRepository(read, new FakeCurrentUserService());
        var byName = await sut.ListAsync(new MediaAssetQuery(Sort: MediaAssetSort.Name));
        var newest = await sut.ListAsync(new MediaAssetQuery(Sort: MediaAssetSort.Newest));

        Assert.Equal([apple, twinA, twinB, zebra], byName.Items.Select(i => i.Id));
        Assert.Equal([twinA, apple, twinB, zebra], newest.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task List_UnusedOnly_LeavesOutImagesAPageVersionUses()
    {
        var used = Guid.NewGuid();
        var unused = Guid.NewGuid();
        var (nodeId, versionId) = await SeedPageVersionAsync();
        try
        {
            await using (var ctx = fixture.CreateContext())
            {
                var sut = new MediaAssetRepository(ctx, new FakeCurrentUserService());
                await sut.CreateAsync(Asset(used));
                await sut.CreateAsync(Asset(unused));
                ctx.MediaUsages.Add(Usage(used, 1, nodeId, versionId));
                await ctx.SaveChangesAsync();
            }

            await using var read = fixture.CreateContext();
            var page = await new MediaAssetRepository(read, new FakeCurrentUserService()).ListAsync(new MediaAssetQuery(UnusedOnly: true));
            Assert.Equal([unused], page.Items.Select(i => i.Id));
        }
        finally
        {
            await DeletePageAsync(nodeId);
        }
    }

    // A used version cannot be deleted, so the database itself backs the library's delete guard.
    [Fact]
    public async Task Database_RefusesToDeleteAnImageVersionThatIsInUse()
    {
        var id = Guid.NewGuid();
        var (nodeId, versionId) = await SeedPageVersionAsync();
        try
        {
            await using (var ctx = fixture.CreateContext())
            {
                await new MediaAssetRepository(ctx, new FakeCurrentUserService()).CreateAsync(Asset(id));
                ctx.MediaUsages.Add(Usage(id, 1, nodeId, versionId));
                await ctx.SaveChangesAsync();
            }

            await using var del = fixture.CreateContext();
            var ex = await Assert.ThrowsAsync<PostgresException>(() =>
                del.MediaAssets.Where(a => a.Id == id).ExecuteDeleteAsync());
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, ex.SqlState);
        }
        finally
        {
            await DeletePageAsync(nodeId);
        }
    }

    // Content import replaces a page's versions by deleting them; their usages go with them.
    [Fact]
    public async Task DeletingAPageVersion_RemovesItsUsages()
    {
        var id = Guid.NewGuid();
        var (nodeId, versionId) = await SeedPageVersionAsync();
        try
        {
            await using (var ctx = fixture.CreateContext())
            {
                await new MediaAssetRepository(ctx, new FakeCurrentUserService()).CreateAsync(Asset(id));
                ctx.MediaUsages.Add(Usage(id, 1, nodeId, versionId));
                await ctx.SaveChangesAsync();
            }

            await using var del = fixture.CreateContext();
            await del.PageNodeVersions.Where(v => v.Id == versionId).ExecuteDeleteAsync();
            Assert.False(await del.MediaUsages.AnyAsync(u => u.AssetId == id));
        }
        finally
        {
            await DeletePageAsync(nodeId);
        }
    }

    [Fact]
    public async Task Database_RefusesAUsageOfAVersionThatDoesNotExist()
    {
        var id = Guid.NewGuid();
        var (nodeId, versionId) = await SeedPageVersionAsync();
        try
        {
            await using var ctx = fixture.CreateContext();
            await new MediaAssetRepository(ctx, new FakeCurrentUserService()).CreateAsync(Asset(id));
            ctx.MediaUsages.Add(Usage(id, 2, nodeId, versionId));
            await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
        }
        finally
        {
            await DeletePageAsync(nodeId);
        }
    }

    // Uploads, replaces and metadata edits reach the audit log through the generic capture; usage
    // rows are derived from page content and are left out.
    [Fact]
    public async Task Audit_CapturesImagesAndVersions_ButNotUsages()
    {
        var id = Guid.NewGuid();
        var (nodeId, versionId) = await SeedPageVersionAsync();
        try
        {
            await using (var ctx = fixture.CreateContext())
            {
                var sut = new MediaAssetRepository(ctx, new FakeCurrentUserService());
                await sut.CreateAsync(Asset(id));
                await sut.AddVersionAsync(id, 2, File(id, 2, 'b'));
                await sut.UpdateMetadataAsync(id, new MediaAssetMetadata("Alt", null, null, ["x"]));
                ctx.MediaUsages.Add(Usage(id, 2, nodeId, versionId));
                await ctx.SaveChangesAsync();
            }

            await using var read = fixture.CreateContext();
            var idText = id.ToString();
            var rows = await read.AuditEntries.AsNoTracking()
                .Where(a => a.EntityType.StartsWith("Media"))
                .Where(a => a.EntityId != null && a.EntityId.StartsWith(idText))
                .ToListAsync();
            Assert.Contains(rows, r => r.EntityType == nameof(MediaAsset) && r.Action == "Insert");
            Assert.Contains(rows, r => r.EntityType == nameof(MediaAsset) && r.Action == "Update" && r.NewValues!.Contains("\"Tags\""));
            Assert.Contains(rows, r => r.EntityType == nameof(MediaAssetVersion) && r.Action == "Insert" && r.EntityId == $"{id},2");
            Assert.False(await read.AuditEntries.AnyAsync(a => a.EntityType == nameof(MediaUsage)));
        }
        finally
        {
            await DeletePageAsync(nodeId);
        }
    }

    // "Used on N pages" counts pages, not page versions or image versions, and a deleted page that
    // still holds the image is listed so the editor can see why the image cannot be deleted.
    [Fact]
    public async Task GetUsagePages_ListsEachPageOnce_ByTitle_AndMarksDeletedPages()
    {
        var id = Guid.NewGuid();
        var (live, liveV1) = await SeedPageVersionAsync("Zebra crossings");
        var (deleted, deletedV1) = await SeedPageVersionAsync("Attendance", deleted: true);
        try
        {
            var liveV2 = await AddPageVersionAsync(live, 2);
            await using (var ctx = fixture.CreateContext())
            {
                var sut = new MediaAssetRepository(ctx, new FakeCurrentUserService());
                await sut.CreateAsync(Asset(id));
                await sut.AddVersionAsync(id, 2, File(id, 2, 'b'));
                ctx.MediaUsages.Add(Usage(id, 1, live, liveV1));
                ctx.MediaUsages.Add(Usage(id, 2, live, liveV2));
                ctx.MediaUsages.Add(Usage(id, 2, live, liveV2, MediaUsageLocations.RichText));
                ctx.MediaUsages.Add(Usage(id, 1, deleted, deletedV1));
                await ctx.SaveChangesAsync();
            }

            await using var read = fixture.CreateContext();
            var sutRead = new MediaAssetRepository(read, new FakeCurrentUserService());
            var pages = await sutRead.GetUsagePagesAsync(id);

            Assert.Collection(pages,
                p => { Assert.Equal(deleted, p.PageNodeId); Assert.Equal("Attendance", p.Title); Assert.True(p.IsDeleted); Assert.True(p.InCurrentVersion); },
                p =>
                {
                    Assert.Equal(live, p.PageNodeId);
                    Assert.Equal("Zebra crossings", p.Title);
                    Assert.False(p.IsDeleted);
                    Assert.Equal($"media-{live:N}", p.Path);
                    // v2 is the working draft; v1 is an older version.
                    Assert.True(p.InCurrentVersion);
                });
            Assert.Empty(await sutRead.GetUsagePagesAsync(Guid.NewGuid()));
        }
        finally
        {
            await DeletePageAsync(live);
            await DeletePageAsync(deleted);
        }
    }

    // A deleted image leaves no rows, and the generic capture records the delete.
    [Fact]
    public async Task Delete_RemovesTheImageAndEveryVersion_AndTheAuditLogRecordsIt()
    {
        var id = Guid.NewGuid();
        var keep = Guid.NewGuid();
        await using (var ctx = fixture.CreateContext())
        {
            var sut = new MediaAssetRepository(ctx, new FakeCurrentUserService());
            await sut.CreateAsync(Asset(id));
            await sut.AddVersionAsync(id, 2, File(id, 2, 'b'));
            await sut.CreateAsync(Asset(keep, sha: 'c'));
        }

        await using (var ctx = fixture.CreateContext())
            Assert.Equal(MediaDeleteOutcome.Deleted, await new MediaAssetRepository(ctx, new FakeCurrentUserService()).DeleteAsync(id));

        await using var read = fixture.CreateContext();
        Assert.False(await read.MediaAssets.AnyAsync(a => a.Id == id));
        Assert.False(await read.MediaAssetVersions.AnyAsync(v => v.AssetId == id));
        Assert.True(await read.MediaAssets.AnyAsync(a => a.Id == keep));
        var idText = id.ToString();
        Assert.True(await read.AuditEntries.AnyAsync(a =>
            a.EntityType == nameof(MediaAsset) && a.Action == "Delete" && a.EntityId == idText));
        Assert.True(await read.AuditEntries.AnyAsync(a =>
            a.EntityType == nameof(MediaAssetVersion) && a.Action == "Delete" && a.EntityId == $"{id},2"));
    }

    // The page here is deleted but its version remains, so the image is still in use.
    [Fact]
    public async Task Delete_IsRefused_WhileAPageVersionUsesTheImage()
    {
        var id = Guid.NewGuid();
        var (nodeId, versionId) = await SeedPageVersionAsync(deleted: true);
        try
        {
            await using (var ctx = fixture.CreateContext())
            {
                await new MediaAssetRepository(ctx, new FakeCurrentUserService()).CreateAsync(Asset(id));
                ctx.MediaUsages.Add(Usage(id, 1, nodeId, versionId));
                await ctx.SaveChangesAsync();
            }

            await using (var ctx = fixture.CreateContext())
                Assert.Equal(MediaDeleteOutcome.InUse, await new MediaAssetRepository(ctx, new FakeCurrentUserService()).DeleteAsync(id));

            await using var read = fixture.CreateContext();
            Assert.True(await read.MediaAssets.AnyAsync(a => a.Id == id));
            Assert.True(await read.MediaAssetVersions.AnyAsync(v => v.AssetId == id));
        }
        finally
        {
            await DeletePageAsync(nodeId);
        }
    }

    [Fact]
    public async Task Delete_ReturnsNotFound_ForAnUnknownImage()
    {
        await using var ctx = fixture.CreateContext();
        Assert.Equal(MediaDeleteOutcome.NotFound,
            await new MediaAssetRepository(ctx, new FakeCurrentUserService()).DeleteAsync(Guid.NewGuid()));
    }

    private static MediaUsage Usage(Guid assetId, int version, Guid nodeId, Guid versionId, string location = MediaUsageLocations.Widget) => new()
    {
        Id = Guid.NewGuid(),
        AssetId = assetId,
        AssetVersion = version,
        PageNodeId = nodeId,
        PageNodeVersionId = versionId,
        Location = location,
        WidgetId = "0.0"
    };

    private async Task<Guid> AddPageVersionAsync(Guid nodeId, int versionNumber)
    {
        var versionId = Guid.NewGuid();
        await using var ctx = fixture.CreateContext();
        ctx.PageNodeVersions.Add(new PageNodeVersion
        {
            Id = versionId,
            PageNodeId = nodeId,
            VersionId = versionNumber,
            Content = "{}",
            CreatedDate = DateTime.UtcNow,
            UpdatedDate = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();
        return versionId;
    }

    private async Task<(Guid NodeId, Guid VersionId)> SeedPageVersionAsync(string title = "Media test page", bool deleted = false)
    {
        var nodeId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var segment = $"media-{nodeId:N}";
        await using var ctx = fixture.CreateContext();
        ctx.PageNodes.Add(new PageNode
        {
            Id = nodeId,
            Segment = segment,
            Path = segment,
            Title = title,
            DeletedDate = deleted ? DateTime.UtcNow : null,
            PageType = "content",
            CreatedDate = DateTime.UtcNow,
            UpdatedDate = DateTime.UtcNow
        });
        ctx.PageNodeVersions.Add(new PageNodeVersion
        {
            Id = versionId,
            PageNodeId = nodeId,
            VersionId = 1,
            Content = "{}",
            CreatedDate = DateTime.UtcNow,
            UpdatedDate = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();
        return (nodeId, versionId);
    }

    private async Task DeletePageAsync(Guid nodeId)
    {
        await using var ctx = fixture.CreateContext();
        await ctx.MediaUsages.Where(u => u.PageNodeId == nodeId).ExecuteDeleteAsync();
        await ctx.PageNodeVersions.Where(v => v.PageNodeId == nodeId).ExecuteDeleteAsync();
        await ctx.PageNodes.IgnoreQueryFilters().Where(n => n.Id == nodeId).ExecuteDeleteAsync();
    }
}
