using System.Text.Json;
using DfE.CheckPerformanceData.Application.Common;
using DfE.CheckPerformanceData.Application.ContentBlocks;
using DfE.CheckPerformanceData.Application.ContentStaging;
using DfE.CheckPerformanceData.Application.Wiki;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.IntegrationTests.ContentStaging;

[Collection(nameof(PostgresCollection))]
public sealed class ContentStagingRoundTripTests(PostgresFixture fixture)
{
    private readonly PostgresFixture _fixture = fixture;
    private static readonly HtmlRenderingService Html = new();

    private ContentStagingService NewStaging(out PortalDbContext ctx)
    {
        ctx = _fixture.CreateContext();
        var user = new FakeCurrentUserService();
        var wikiRepo = new WikiRepository(ctx, user);
        var blockRepo = new ContentBlockRepository(ctx);
        var wikiSvc = new WikiService(wikiRepo, Html);
        return new ContentStagingService(wikiSvc, wikiRepo, blockRepo);
    }

    private async Task ResetAsync()
    {
        await using var ctx = _fixture.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync(
            @"TRUNCATE ""WikiPages"", ""ContentBlocks"" RESTART IDENTITY CASCADE;");
    }

    private async Task SeedAsync()
    {
        await using var ctx = _fixture.CreateContext();
        var wikiSvc = new WikiService(new WikiRepository(ctx, new FakeCurrentUserService()), Html);
        var blockSvc = new ContentBlockService(new ContentBlockRepository(ctx), Html);

        var alpha = await wikiSvc.CreatePageAsync(new CreateWikiPageDto { Title = "Alpha", Content = "## Alpha body" });
        await wikiSvc.CreatePageAsync(new CreateWikiPageDto { Title = "Beta", Content = "## Beta body", ParentId = alpha.Id });
        await wikiSvc.CreatePageAsync(new CreateWikiPageDto { Title = "Gamma", Content = "## Gamma body" });

        await blockSvc.SaveAsync(new SaveContentBlockDto { Key = "footer", BlockType = "Content", Value = "## footer" });
        await blockSvc.SaveAsync(new SaveContentBlockDto { Key = "banner", BlockType = "Content", Value = "## banner" });
    }

    [Fact]
    public async Task Export_ThenImportIntoFreshDb_ThenExport_ProducesIdenticalPayload()
    {
        await ResetAsync();
        await SeedAsync();

        ContentBundle firstExport;
        {
            var staging = NewStaging(out var ctx);
            await using var _ = ctx;
            firstExport = await staging.ExportAsync();
        }

        // Sanity: the bundle carries the seeded content with GUID parentage (Beta's parent is Alpha).
        Assert.Equal(3, firstExport.WikiPages.Count);
        Assert.Equal(2, firstExport.ContentBlocks.Count);
        var alphaId = firstExport.WikiPages.Single(p => p.Title == "Alpha").Id;
        Assert.Contains(firstExport.WikiPages, p => p.Title == "Beta" && p.ParentId == alphaId);

        // Fresh environment: wipe everything, replay the bundle through the app layer.
        await ResetAsync();
        ContentImportResult importResult;
        {
            var staging = NewStaging(out var ctx);
            await using var _ = ctx;
            importResult = await staging.ImportAsync(firstExport, ContentImportMode.Skip);
        }
        Assert.Equal(3, importResult.WikiPagesCreated);
        Assert.Equal(2, importResult.ContentBlocksCreated);
        Assert.Empty(importResult.Warnings);

        ContentBundle secondExport;
        {
            var staging = NewStaging(out var ctx);
            await using var _ = ctx;
            secondExport = await staging.ExportAsync();
        }

        // Round-trip integrity: the canonical payload is identical on the second run. Compare the
        // serialised content arrays (the bundle's header metadata is excluded by construction —
        // ExportAsync leaves ExportedAtUtc / ExportedBy unset).
        Assert.True(firstExport.WikiPages.SequenceEqual(secondExport.WikiPages));
        Assert.True(firstExport.ContentBlocks.SequenceEqual(secondExport.ContentBlocks));
        Assert.Equal(
            JsonSerializer.Serialize(firstExport.WikiPages, ContentStagingJson.Options),
            JsonSerializer.Serialize(secondExport.WikiPages, ContentStagingJson.Options));
    }

    [Fact]
    public async Task Import_Skip_DoesNotDuplicateExistingContent()
    {
        await ResetAsync();
        await SeedAsync();

        ContentBundle bundle;
        {
            var staging = NewStaging(out var ctx);
            await using var _ = ctx;
            bundle = await staging.ExportAsync();
        }

        // Re-import into the SAME populated DB: everything already exists, so nothing is added.
        ContentImportResult result;
        {
            var staging = NewStaging(out var ctx);
            await using var _ = ctx;
            result = await staging.ImportAsync(bundle, ContentImportMode.Skip);
        }

        Assert.Equal(0, result.WikiPagesCreated);
        Assert.Equal(3, result.WikiPagesSkipped);
        Assert.Equal(0, result.ContentBlocksCreated);
        Assert.Equal(2, result.ContentBlocksSkipped);

        await using var verify = _fixture.CreateContext();
        Assert.Equal(3, await verify.WikiPages.CountAsync());
        Assert.Equal(2, await verify.ContentBlocks.CountAsync());
    }

    [Fact]
    public async Task Import_Fail_IntoPopulatedDb_Throws_AndAddsNothing()
    {
        await ResetAsync();
        await SeedAsync();

        ContentBundle bundle;
        {
            var staging = NewStaging(out var ctx);
            await using var _ = ctx;
            bundle = await staging.ExportAsync();
        }

        var staging2 = NewStaging(out var ctx2);
        await using var __ = ctx2;
        await Assert.ThrowsAsync<ContentImportConflictException>(
            () => staging2.ImportAsync(bundle, ContentImportMode.Fail));
    }

    [Fact]
    public async Task Import_Replace_MatchesRenamedPageByGuid_NotSlug()
    {
        // Lance's case: source has A + B. Target also had A + B, but B was renamed to C (the row
        // keeps its GUID). Pushing source -> target with Replace must overwrite C (same GUID as B),
        // restoring the title to "B" — proving identity is the GUID, not the slug/title.
        await ResetAsync();

        // Source environment: A + B. Capture the bundle.
        ContentBundle bundle;
        {
            var staging = NewStaging(out var ctx);
            await using var _ = ctx;
            var wikiSvc = new WikiService(new WikiRepository(ctx, new FakeCurrentUserService()), Html);
            await wikiSvc.CreatePageAsync(new CreateWikiPageDto { Title = "A doc", Content = "a" });
            await wikiSvc.CreatePageAsync(new CreateWikiPageDto { Title = "B doc", Content = "b-source" });
            bundle = await staging.ExportAsync();
        }
        var bGuid = bundle.WikiPages.Single(p => p.Title == "B doc").Id;

        // Target environment: same DB, but "B doc" gets renamed to "C doc" (GUID unchanged).
        await using (var ctx = _fixture.CreateContext())
        {
            var wikiSvc = new WikiService(new WikiRepository(ctx, new FakeCurrentUserService()), Html);
            var bRow = await ctx.WikiPages.FirstAsync(p => p.ContentId == bGuid);
            await wikiSvc.UpdatePageAsync(bRow.Id, new UpdateWikiPageDto { Title = "C doc", Content = "c-edited" });
        }

        // Push source -> target with Replace.
        {
            var staging = NewStaging(out var ctx);
            await using var _ = ctx;
            var result = await staging.ImportAsync(bundle, ContentImportMode.Replace);
            Assert.Equal(2, result.WikiPagesUpdated);   // both matched by GUID, none created
            Assert.Equal(0, result.WikiPagesCreated);
        }

        // The renamed row was matched by GUID and restored to the source's title/content.
        await using (var verify = _fixture.CreateContext())
        {
            Assert.Equal(2, await verify.WikiPages.CountAsync());          // no duplicate created
            var restored = await verify.WikiPages.FirstAsync(p => p.ContentId == bGuid);
            Assert.Equal("B doc", restored.Title);
            Assert.Equal("b-source", restored.Content);
        }
    }

    [Fact]
    public async Task ContentBlockRepository_GetAllAsync_ReturnsAllBlocksOrderedByKey()
    {
        await ResetAsync();
        await SeedAsync();

        await using var ctx = _fixture.CreateContext();
        var repo = new ContentBlockRepository(ctx);
        var all = await repo.GetAllAsync();

        Assert.Equal(["banner", "footer"], all.Select(b => b.Key));
    }

    // ---- export/import scenarios verifying the resulting state -------------------------------

    private async Task<ContentBundle> ExportAsync(ContentExportSelection? selection = null)
    {
        var staging = NewStaging(out var ctx);
        await using var _ = ctx;
        return await staging.ExportAsync(selection);
    }

    private async Task<ContentImportResult> ImportAsync(
        ContentBundle bundle, ContentImportMode mode, IReadOnlyDictionary<Guid, ContentImportMode>? decisions = null)
    {
        var staging = NewStaging(out var ctx);
        await using var _ = ctx;
        return await staging.ImportAsync(bundle, mode, decisions);
    }

    [Fact]
    public async Task Export_ThenImportFresh_ReproducesTree_Parentage_Order_AndContent()
    {
        await ResetAsync();
        await SeedAsync();
        var bundle = await ExportAsync();

        await ResetAsync();
        var result = await ImportAsync(bundle, ContentImportMode.Skip);

        Assert.Equal(3, result.WikiPagesCreated);
        Assert.Empty(result.Errors);

        await using var verify = _fixture.CreateContext();
        var pages = await verify.WikiPages.OrderBy(p => p.Id).ToListAsync();
        Assert.Equal(3, pages.Count);

        var alpha = pages.Single(p => p.Title == "Alpha");
        var beta = pages.Single(p => p.Title == "Beta");
        Assert.Null(alpha.ParentId);
        Assert.Equal(alpha.Id, beta.ParentId);              // parentage reproduced via GUID
        Assert.Equal("## Beta body", beta.Content);         // content reproduced
        Assert.Equal("alpha", alpha.Slug);                  // slug regenerated from title
    }

    [Fact]
    public async Task Export_SelectOnlyOneBlock_BundleHasNoPagesAndThatBlock()
    {
        await ResetAsync();
        await SeedAsync();
        var full = await ExportAsync();
        var footerId = full.ContentBlocks.Single(b => b.Key == "footer").Id;

        var bundle = await ExportAsync(new ContentExportSelection(new HashSet<Guid>(), new HashSet<Guid> { footerId }));

        Assert.Empty(bundle.WikiPages);
        Assert.Equal(["footer"], bundle.ContentBlocks.Select(b => b.Key));
    }

    [Fact]
    public async Task Import_Replace_RestoresEditedContent()
    {
        await ResetAsync();
        await SeedAsync();
        var bundle = await ExportAsync();

        // Drift the live content away from the bundle.
        await using (var ctx = _fixture.CreateContext())
        {
            var alpha = await ctx.WikiPages.FirstAsync(p => p.Title == "Alpha");
            alpha.Content = "DRIFTED";
            await ctx.SaveChangesAsync();
        }

        var result = await ImportAsync(bundle, ContentImportMode.Replace);

        Assert.Equal(3, result.WikiPagesUpdated);
        await using var verify = _fixture.CreateContext();
        var alphaAfter = await verify.WikiPages.FirstAsync(p => p.Title == "Alpha");
        Assert.Equal("## Alpha body", alphaAfter.Content);   // restored from the bundle
    }

    [Fact]
    public async Task Import_PerItemDecisions_OverwriteOneSkipAnother()
    {
        await ResetAsync();
        await SeedAsync();
        var bundle = await ExportAsync();

        Guid alphaId, gammaId;
        await using (var ctx = _fixture.CreateContext())
        {
            var alpha = await ctx.WikiPages.FirstAsync(p => p.Title == "Alpha");
            var gamma = await ctx.WikiPages.FirstAsync(p => p.Title == "Gamma");
            alphaId = alpha.ContentId;
            gammaId = gamma.ContentId;
            alpha.Content = "DRIFTED-A";
            gamma.Content = "DRIFTED-G";
            await ctx.SaveChangesAsync();
        }

        // Global Skip, but overwrite Alpha specifically.
        var decisions = new Dictionary<Guid, ContentImportMode> { [alphaId] = ContentImportMode.Replace };
        await ImportAsync(bundle, ContentImportMode.Skip, decisions);

        await using var verify = _fixture.CreateContext();
        Assert.Equal("## Alpha body", (await verify.WikiPages.FirstAsync(p => p.Title == "Alpha")).Content); // overwritten
        Assert.Equal("DRIFTED-G", (await verify.WikiPages.FirstAsync(p => p.Title == "Gamma")).Content);     // skipped
    }

    [Fact]
    public async Task Import_UnknownParent_ReportsError_AndCreatesNoOrphan()
    {
        await ResetAsync();
        await SeedAsync();

        var bundle = new ContentBundle
        {
            WikiPages = [new() { Id = Guid.NewGuid(), ParentId = Guid.NewGuid(), Title = "Orphan", Content = "x" }]
        };

        var result = await ImportAsync(bundle, ContentImportMode.Skip);

        Assert.Single(result.Errors);
        Assert.Equal(0, result.WikiPagesCreated);
        await using var verify = _fixture.CreateContext();
        Assert.Equal(3, await verify.WikiPages.CountAsync());        // no orphan added
        Assert.False(await verify.WikiPages.AnyAsync(p => p.Title == "Orphan"));
    }

    [Fact]
    public async Task Import_NewBlock_WithExistingKey_ReportsError_AndDoesNotOverwrite()
    {
        await ResetAsync();
        await SeedAsync();

        // New identity, but a Key that already exists (independently seeded) — must not merge.
        var bundle = new ContentBundle
        {
            ContentBlocks = [new() { Id = Guid.NewGuid(), Key = "footer", BlockType = "Content", Value = "HIJACK" }]
        };

        var result = await ImportAsync(bundle, ContentImportMode.Replace);

        Assert.Single(result.Errors);
        Assert.Equal(0, result.ContentBlocksUpdated);
        await using var verify = _fixture.CreateContext();
        Assert.Equal("## footer", (await verify.ContentBlocks.FirstAsync(b => b.Key == "footer")).Value); // untouched
    }

    [Fact]
    public async Task Preview_ReportsNew_Collision_AndBlockedCounts()
    {
        await ResetAsync();
        await SeedAsync();
        var bundle = await ExportAsync();   // 3 pages + 2 blocks, all will collide

        bundle.WikiPages.Add(new() { Id = Guid.NewGuid(), Title = "Brand New" });                               // new
        bundle.WikiPages.Add(new() { Id = Guid.NewGuid(), ParentId = Guid.NewGuid(), Title = "Orphan" });       // blocked

        var staging = NewStaging(out var ctx);
        await using var _ = ctx;
        var preview = await staging.PreviewAsync(bundle);

        Assert.Equal(5, preview.CollisionCount);     // 3 pages + 2 blocks
        Assert.Equal(1, preview.NewCount);           // Brand New
        Assert.Equal(1, preview.BlockedCount);       // Orphan
    }
}
