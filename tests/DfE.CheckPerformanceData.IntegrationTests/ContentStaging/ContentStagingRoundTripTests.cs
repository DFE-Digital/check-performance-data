using DfE.CheckPerformanceData.Application.ContentBlocks;
using DfE.CheckPerformanceData.Application.ContentStaging;
using DfE.CheckPerformanceData.Application.PageTree;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.IntegrationTests.ContentStaging;

// End-to-end round-trip through a real Postgres: seed a page tree with folders, content pages
// and multiple versions; export; wipe the database; import; export again; assert the second
// export matches the first structurally. Proves the v2 bundle is a complete, invertible snapshot
// of the page tree and content blocks — no drift on Id, ParentId, Segment, Title, SortOrder,
// PageType, per-node version history, or content-block payloads.
[Collection(nameof(PostgresCollection))]
public sealed class ContentStagingRoundTripTests(PostgresFixture fixture)
{
    private readonly PostgresFixture _fixture = fixture;

    private ContentStagingService NewStaging(out PortalDbContext ctx)
    {
        ctx = _fixture.CreateContext();
        var pageRepo = new PageNodeRepository(ctx);
        var blockRepo = new ContentBlockRepository(ctx);
        return new ContentStagingService(pageRepo, blockRepo);
    }

    private async Task ResetAsync()
    {
        await using var ctx = _fixture.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync(
            @"TRUNCATE ""PageNodes"", ""PageNodeVersions"", ""ContentBlocks"", ""ContentBlockVersions"" RESTART IDENTITY CASCADE;");
    }

    // Seeds a tiny tree:
    //   support (folder)
    //     └─ getting-started (content, 2 versions — first published, working draft on top)
    //   guidance (folder)
    //     └─ ks4-window (content, 1 published version)
    // Plus two content blocks (banner, footer). All identities are stable across the two exports.
    private async Task SeedAsync()
    {
        await using var ctx = _fixture.CreateContext();
        var pageRepo = new PageNodeRepository(ctx);
        var pageSvc = new PageNodeService(pageRepo);
        var blockRepo = new ContentBlockRepository(ctx);

        var support = await pageSvc.CreatePageAsync(null, "support", "Support", "folder", userId: "seed");
        var start = await pageSvc.CreatePageAsync(
            support.Id, "getting-started", "Getting started", "content", userId: "seed");
        await pageSvc.SaveWorkingContentAsync(start.Id, "{\"kind\":\"root\",\"children\":[]}", "getting started", "seed");
        await pageSvc.PublishDraftAsync(start.Id, userId: "seed");
        await pageSvc.SaveWorkingContentAsync(start.Id, "{\"kind\":\"root\",\"children\":[\"draft\"]}", "getting started draft", "seed");

        var guidance = await pageSvc.CreatePageAsync(null, "guidance", "Guidance", "folder", userId: "seed");
        var ks4 = await pageSvc.CreatePageAsync(
            guidance.Id, "ks4-window", "KS4 window", "content", userId: "seed");
        await pageSvc.SaveWorkingContentAsync(ks4.Id, "{\"kind\":\"root\",\"children\":[\"ks4\"]}", "ks4 window", "seed");
        await pageSvc.PublishDraftAsync(ks4.Id, userId: "seed");

        await blockRepo.ExecuteInTransactionAsync(async () =>
        {
            var banner = await blockRepo.AddBlockAsync("banner", "Content", "hello", Guid.NewGuid());
            await blockRepo.AddVersionAsync(banner.Id, "hello", 1);
            var footer = await blockRepo.AddBlockAsync("footer", "Content", "© crown", Guid.NewGuid());
            await blockRepo.AddVersionAsync(footer.Id, "© crown", 1);
        });
    }

    [Fact]
    public async Task Export_ThenImportIntoFreshDb_ThenExport_ProducesEquivalentBundle()
    {
        await ResetAsync();
        await SeedAsync();

        ContentBundle firstExport;
        {
            var staging = NewStaging(out var ctx);
            await using var _ = ctx;
            firstExport = await staging.ExportAsync();
        }

        // Sanity: bundle carries the seeded tree with GUID parentage (getting-started → support).
        Assert.Equal(4, firstExport.PageNodes.Count);
        Assert.Equal(2, firstExport.ContentBlocks.Count);
        var supportId = firstExport.PageNodes.Single(p => p.Segment == "support").Id;
        Assert.Contains(firstExport.PageNodes, p => p.Segment == "getting-started" && p.ParentId == supportId);

        // Content pages carry version history; folders do not.
        Assert.All(
            firstExport.PageNodes.Where(p => p.PageType == "folder"),
            f => Assert.Empty(f.Versions));
        var start = firstExport.PageNodes.Single(p => p.Segment == "getting-started");
        Assert.True(start.Versions.Count >= 2, "getting-started must carry both the published version and the working draft");

        // Fresh environment: wipe everything, replay the bundle through the service.
        await ResetAsync();
        ContentImportResult importResult;
        {
            var staging = NewStaging(out var ctx);
            await using var _ = ctx;
            importResult = await staging.ImportAsync(firstExport, ContentImportMode.Skip);
        }
        Assert.Equal(4, importResult.PageNodesCreated);
        Assert.Equal(2, importResult.ContentBlocksCreated);
        Assert.Empty(importResult.Errors);

        // Re-export from the fresh environment and assert structural equality with the original.
        ContentBundle secondExport;
        {
            var staging = NewStaging(out var ctx);
            await using var _ = ctx;
            secondExport = await staging.ExportAsync();
        }

        AssertPagesEquivalent(firstExport.PageNodes, secondExport.PageNodes);
        AssertBlocksEquivalent(firstExport.ContentBlocks, secondExport.ContentBlocks);
    }

    [Fact]
    public async Task ImportAsync_ExistingIdSkipMode_LeavesTargetUnchanged()
    {
        await ResetAsync();
        await SeedAsync();

        ContentBundle firstExport;
        {
            var staging = NewStaging(out var ctx);
            await using var _ = ctx;
            firstExport = await staging.ExportAsync();
        }

        // Re-import into the SAME database with Skip mode — every collision resolves to Skip, so
        // no node is created and no version is replaced.
        ContentImportResult result;
        {
            var staging = NewStaging(out var ctx);
            await using var _ = ctx;
            result = await staging.ImportAsync(firstExport, ContentImportMode.Skip);
        }

        Assert.Equal(0, result.PageNodesCreated);
        Assert.Equal(4, result.PageNodesSkipped);
    }

    // ── assertion helpers ──────────────────────────────────────────────────

    private static void AssertPagesEquivalent(
        IReadOnlyList<PageNodeBundleItem> a, IReadOnlyList<PageNodeBundleItem> b)
    {
        Assert.Equal(a.Count, b.Count);
        var aById = a.ToDictionary(p => p.Id);
        var bById = b.ToDictionary(p => p.Id);
        Assert.Equal(aById.Keys.OrderBy(x => x), bById.Keys.OrderBy(x => x));

        foreach (var id in aById.Keys)
        {
            var pa = aById[id];
            var pb = bById[id];
            Assert.Equal(pa.ParentId, pb.ParentId);
            Assert.Equal(pa.Segment, pb.Segment);
            Assert.Equal(pa.Title, pb.Title);
            Assert.Equal(pa.PageType, pb.PageType);
            Assert.Equal(pa.SortOrder, pb.SortOrder);
            Assert.Equal(pa.Versions.Count, pb.Versions.Count);

            for (int i = 0; i < pa.Versions.Count; i++)
            {
                Assert.Equal(pa.Versions[i].VersionId, pb.Versions[i].VersionId);
                Assert.Equal(pa.Versions[i].MinorVersion, pb.Versions[i].MinorVersion);
                Assert.Equal(pa.Versions[i].PublishFrom, pb.Versions[i].PublishFrom);
                Assert.Equal(pa.Versions[i].PublishTo, pb.Versions[i].PublishTo);
                Assert.Equal(pa.Versions[i].Content, pb.Versions[i].Content);
                Assert.Equal(pa.Versions[i].BodyPlainText, pb.Versions[i].BodyPlainText);
            }
        }
    }

    private static void AssertBlocksEquivalent(
        IReadOnlyList<ContentBlockBundleItem> a, IReadOnlyList<ContentBlockBundleItem> b)
    {
        Assert.Equal(a.Count, b.Count);
        var aById = a.ToDictionary(x => x.Id);
        var bById = b.ToDictionary(x => x.Id);
        Assert.Equal(aById.Keys.OrderBy(x => x), bById.Keys.OrderBy(x => x));

        foreach (var id in aById.Keys)
        {
            Assert.Equal(aById[id].Key, bById[id].Key);
            Assert.Equal(aById[id].BlockType, bById[id].BlockType);
            Assert.Equal(aById[id].Value, bById[id].Value);
        }
    }
}
