using DfE.CheckPerformanceData.Application.Common;
using DfE.CheckPerformanceData.Application.ContentStaging;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.PageTree;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace DfE.CheckPerformanceData.IntegrationTests.ContentStaging;

// A version that a newer version replaced stays replaced through export and import. The rows get
// new ids on import, so the bundle names the replacing version by its number in the page.
[Collection(nameof(PostgresCollection))]
public sealed class ContentStagingSupersededTests(PostgresFixture fixture) : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await using var ctx = fixture.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync(
            @"TRUNCATE ""PageNodes"", ""PageNodeVersions"", ""ContentBlocks"", ""ContentBlockVersions"", ""HomeBanners"", ""HomeBannerVersions"" RESTART IDENTITY CASCADE;");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private ContentStagingService NewStaging(PortalDbContext ctx) =>
        new(new PageNodeRepository(ctx), new ContentBlockRepository(ctx),
            new HomeBannerRepository(ctx, Substitute.For<ICurrentUserService>()), new HtmlRenderingService());

    // Live v1, replaced by live v2 on the same window.
    private async Task<Guid> PageWithAReplacedVersionAsync()
    {
        Guid page;
        await using (var ctx = fixture.CreateContext())
        {
            var service = new PageNodeService(new PageNodeRepository(ctx));
            page = (await service.CreatePageAsync(null, "replaced", "Replaced", "content", "seed")).Id;
            await service.SaveWorkingContentAsync(page, "[\"old\"]", "", "seed");
            await service.PublishDraftAsync(page, "seed");
        }
        await using (var ctx = fixture.CreateContext())
        {
            var old = await ctx.PageNodeVersions.SingleAsync(v => v.PageNodeId == page);
            var replacement = new PageNodeVersion
            {
                Id = Guid.NewGuid(), PageNodeId = page, VersionId = 2, Content = "[\"new\"]",
                PublishFrom = old.PublishFrom, IsCurrent = true,
                CreatedDate = DateTime.UtcNow, UpdatedDate = DateTime.UtcNow
            };
            ctx.PageNodeVersions.Add(replacement);
            old.SupersededById = replacement.Id;
            old.IsCurrent = false;
            await ctx.SaveChangesAsync();
        }
        return page;
    }

    [Fact]
    public async Task ExportAndImport_KeepWhichVersionWasReplaced()
    {
        var page = await PageWithAReplacedVersionAsync();

        ContentBundle bundle;
        await using (var ctx = fixture.CreateContext())
            bundle = await NewStaging(ctx).ExportAsync(new ContentExportSelection(MaxVersionsPerNode: null));
        var exported = bundle.PageNodes.Single(p => p.Id == page).Versions;
        Assert.Equal(2, exported.Single(v => v.VersionId == 1).SupersededByVersionId);
        Assert.Null(exported.Single(v => v.VersionId == 2).SupersededByVersionId);

        // Replace deletes the page's versions, replaced ones included, and adds the bundle's.
        await using (var ctx = fixture.CreateContext())
            await NewStaging(ctx).ImportAsync(bundle, ContentImportMode.Replace);

        await using (var ctx = fixture.CreateContext())
        {
            var versions = await ctx.PageNodeVersions.AsNoTracking()
                .Where(v => v.PageNodeId == page).OrderBy(v => v.VersionId).ToListAsync();
            Assert.Equal(versions[1].Id, versions[0].SupersededById);
            Assert.Null(versions[1].SupersededById);

            var service = new PageNodeService(new PageNodeRepository(ctx));
            await service.UnpublishAsync(page, "test");
        }

        // Unpublishing the new version does not bring the replaced one back.
        await using (var ctx = fixture.CreateContext())
            Assert.Null(await new PageNodeRepository(ctx).GetLiveVersionAsync(page, DateTime.UtcNow));
    }
}
