using DfE.CheckPerformanceData.Application.Common;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.HomeBanners;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace DfE.CheckPerformanceData.IntegrationTests.RetriedTransactions;

// #757: the home banner repository wrapper must start each run of a retried delegate with a
// clean change tracker.
[Collection(nameof(PostgresCollection))]
public sealed class HomeBannerRetryTests(PostgresFixture fixture)
{
    private readonly PostgresFixture _fixture = fixture;

    private async Task ResetAsync()
    {
        await using var ctx = _fixture.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync(
            @"TRUNCATE ""HomeBanners"", ""HomeBannerVersions"", ""AuditEntries"" RESTART IDENTITY CASCADE;");
    }

    private static HomeBannerService NewService(PortalDbContext ctx) =>
        new(new HomeBannerRepository(ctx, Substitute.For<ICurrentUserService>()),
            new HtmlRenderingService(), TimeProvider.System);

    [Fact]
    public async Task Create_that_is_retried_after_a_transient_fault_makes_one_banner()
    {
        await ResetAsync();
        var interceptor = new FailFirstCommandInterceptor("INSERT INTO \"HomeBanners\"");
        await using var ctx = _fixture.CreateRetryingContext(interceptor);

        var created = await NewService(ctx).CreateAsync(new HomeBannerContent("Heading", "<p>Body</p>", true, null, null));

        Assert.Equal(2, interceptor.Matches);
        await using var verify = _fixture.CreateContext();
        var banner = await verify.HomeBanners.SingleAsync();
        Assert.Equal(created.Id, banner.Id);
        var version = await verify.HomeBannerVersions.SingleAsync();
        Assert.Equal(banner.Id, version.HomeBannerId);
        Assert.Equal(1, version.VersionNumber);
    }

    [Fact]
    public async Task Update_that_is_retried_after_a_failed_commit_keeps_the_new_heading()
    {
        await ResetAsync();
        int id;
        await using (var seed = _fixture.CreateContext())
        {
            id = (await NewService(seed).CreateAsync(new HomeBannerContent("Old heading", "<p>Body</p>", true, null, null))).Id;
        }

        var interceptor = new FailFirstCommitInterceptor();
        await using var ctx = _fixture.CreateRetryingContext(interceptor);

        await NewService(ctx).UpdateAsync(id, new HomeBannerContent("New heading", "<p>Body</p>", true, null, null));

        Assert.Equal(2, interceptor.Commits);
        await using var verify = _fixture.CreateContext();
        Assert.Equal("New heading", (await verify.HomeBanners.SingleAsync(b => b.Id == id)).Heading);
        var versions = await verify.HomeBannerVersions
            .Where(v => v.HomeBannerId == id).OrderBy(v => v.VersionNumber).ToListAsync();
        Assert.Equal([1, 2], versions.Select(v => v.VersionNumber));
        Assert.Equal("New heading", versions[1].Heading);
    }
}
