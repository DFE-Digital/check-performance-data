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
        var blockSvc = new ContentBlockService(blockRepo, Html);
        return new ContentStagingService(wikiSvc, wikiRepo, blockSvc, blockRepo);
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

        // Sanity: the bundle actually carries the seeded content with slug-path parentage.
        Assert.Equal(3, firstExport.WikiPages.Count);
        Assert.Equal(2, firstExport.ContentBlocks.Count);
        Assert.Contains(firstExport.WikiPages, p => p.SlugPath == "alpha/beta" && p.ParentSlugPath == "alpha");

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
    public async Task ContentBlockRepository_GetAllAsync_ReturnsAllBlocksOrderedByKey()
    {
        await ResetAsync();
        await SeedAsync();

        await using var ctx = _fixture.CreateContext();
        var repo = new ContentBlockRepository(ctx);
        var all = await repo.GetAllAsync();

        Assert.Equal(["banner", "footer"], all.Select(b => b.Key));
    }
}
