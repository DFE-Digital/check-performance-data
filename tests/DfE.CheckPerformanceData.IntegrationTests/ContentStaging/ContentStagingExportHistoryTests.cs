using DfE.CheckPerformanceData.Application.Common;
using DfE.CheckPerformanceData.Application.ContentStaging;
using DfE.CheckPerformanceData.Application.PageTree;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.IntegrationTests.ContentStaging;

// The version cap, exercised through the real repository rather than a mocked one.
//
// The service unit tests pin the trimming rule itself; these prove the whole export path
// honours it against a page whose history was built the way the CMS builds it — save a draft,
// publish it, repeat — so a change to how versions are stored or read cannot quietly restore
// the unbounded export.
//
// A real environment export from the test tier carried pages at 20, 15 and 13 versions, which
// is what the depth below is modelled on.
[Collection(nameof(PostgresCollection))]
public sealed class ContentStagingExportHistoryTests(PostgresFixture fixture)
{
    private readonly PostgresFixture _fixture = fixture;

    private async Task ResetAsync()
    {
        await using var ctx = _fixture.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync(
            @"TRUNCATE ""PageNodes"", ""PageNodeVersions"", ""ContentBlocks"", ""ContentBlockVersions"" RESTART IDENTITY CASCADE;");
    }

    private ContentStagingService NewStaging(out PortalDbContext ctx)
    {
        ctx = _fixture.CreateContext();
        return new ContentStagingService(
            new PageNodeRepository(ctx), new ContentBlockRepository(ctx), new HtmlRenderingService());
    }

    // Builds one content page carrying `versions` published versions.
    private async Task<Guid> SeedDeepPageAsync(int versions)
    {
        await using var ctx = _fixture.CreateContext();
        var pageSvc = new PageNodeService(new PageNodeRepository(ctx));

        var page = await pageSvc.CreatePageAsync(null, "deep", "Deep page", "content", userId: "seed");
        for (var i = 1; i <= versions; i++)
        {
            await pageSvc.SaveWorkingContentAsync(
                page.Id, $"{{\"kind\":\"root\",\"children\":[\"v{i}\"]}}", $"revision {i}", "seed");
            await pageSvc.PublishDraftAsync(page.Id, userId: "seed");
        }
        return page.Id;
    }

    [Fact]
    public async Task Export_ByDefault_CapsADeepPagesHistory()
    {
        await ResetAsync();
        var pageId = await SeedDeepPageAsync(versions: 20);

        var staging = NewStaging(out var ctx);
        await using (ctx)
        {
            var bundle = await staging.ExportAsync();

            var page = bundle.PageNodes.Single(p => p.Id == pageId);
            Assert.InRange(page.Versions.Count, 1, ContentExportSelection.DefaultMaxVersionsPerNode + 1);
        }
    }

    [Fact]
    public async Task Export_WithFullHistory_CarriesEveryVersionOfADeepPage()
    {
        await ResetAsync();
        var pageId = await SeedDeepPageAsync(versions: 20);

        var staging = NewStaging(out var ctx);
        await using (ctx)
        {
            var bundle = await staging.ExportAsync(
                new ContentExportSelection(MaxVersionsPerNode: null));

            Assert.Equal(20, bundle.PageNodes.Single(p => p.Id == pageId).Versions.Count);
        }
    }

    // The comparison an operator would make: same environment, same moment, one checkbox apart.
    [Fact]
    public async Task Export_CappedVersusFull_DiffersOnlyInHistoryDepth()
    {
        await ResetAsync();
        var pageId = await SeedDeepPageAsync(versions: 20);

        var staging = NewStaging(out var ctx);
        await using (ctx)
        {
            var capped = await staging.ExportAsync();
            var full = await staging.ExportAsync(new ContentExportSelection(MaxVersionsPerNode: null));

            // Same pages either way — the cap drops versions, never content.
            Assert.Equal(
                full.PageNodes.Select(p => p.Id).Order(),
                capped.PageNodes.Select(p => p.Id).Order());
            Assert.True(
                capped.PageNodes.Sum(p => p.Versions.Count) < full.PageNodes.Sum(p => p.Versions.Count),
                "the capped export should carry fewer versions than the full one");

            // Whatever the capped export kept is the tail of the full history, in the same order.
            var cappedPage = capped.PageNodes.Single(p => p.Id == pageId);
            var fullPage = full.PageNodes.Single(p => p.Id == pageId);
            Assert.Equal(
                fullPage.Versions.Select(v => v.VersionId).TakeLast(cappedPage.Versions.Count),
                cappedPage.Versions.Select(v => v.VersionId));
        }
    }

    // A trimmed export still has to describe what the environment is actually serving, or the
    // target renders something different from the source.
    [Fact]
    public async Task Export_ByDefault_KeepsTheVersionTheEnvironmentIsServing()
    {
        await ResetAsync();
        var pageId = await SeedDeepPageAsync(versions: 20);

        await using var probe = _fixture.CreateContext();
        var live = await probe.PageNodeVersions
            .AsNoTracking()
            .Where(v => v.PageNodeId == pageId && v.IsCurrent)
            .Select(v => (int?)v.VersionId)
            .FirstOrDefaultAsync();
        Assert.NotNull(live);

        var staging = NewStaging(out var ctx);
        await using (ctx)
        {
            var bundle = await staging.ExportAsync();

            Assert.Contains(live!.Value, bundle.PageNodes.Single(p => p.Id == pageId).Versions.Select(v => v.VersionId));
        }
    }
}
