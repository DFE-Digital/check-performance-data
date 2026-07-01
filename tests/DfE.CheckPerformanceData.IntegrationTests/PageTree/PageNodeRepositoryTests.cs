using DfE.CheckPerformanceData.Application.PageTree;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Npgsql;

namespace DfE.CheckPerformanceData.IntegrationTests.PageTree;

[Collection(nameof(PostgresCollection))]
public sealed class PageNodeRepositoryTests(PostgresFixture fixture)
{
    private readonly PostgresFixture _fixture = fixture;
    private static readonly DateTime Now = DateTime.UtcNow;

    private async Task TruncateAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"TRUNCATE ""PageNodeVersions"", ""PageNodes"" RESTART IDENTITY CASCADE;";
        await cmd.ExecuteNonQueryAsync();
    }
    private PageNodeRepository Repo() => new(_fixture.CreateContext());

    [Fact]
    public async Task Create_Then_GetByPath_And_Tree()
    {
        await TruncateAsync();
        var support = await Repo().CreateNodeAsync(null, "support", "support", "Support", "folder", "u1");
        await Repo().CreateNodeAsync(support.Id, "faq", "support/faq", "FAQ", "content", "u1");

        Assert.NotNull(await Repo().GetByPathAsync("support/faq"));
        Assert.Equal(2, (await Repo().GetTreeAsync()).Count);
    }

    [Fact]
    public async Task AddVersion_SchedulesAndMarksCurrent()
    {
        await TruncateAsync();
        var n = await Repo().CreateNodeAsync(null, "p", "p", "P", "content", "u1");

        var v1 = await Repo().AddVersionAsync(n.Id, "[]", "", Now.AddMinutes(-1), null, "u1");
        Assert.Equal(1, v1);

        var live = await Repo().GetLiveVersionAsync(n.Id, Now);
        Assert.NotNull(live);
        Assert.True(live!.IsCurrent);
        Assert.Equal(1, live.VersionId);
    }

    [Fact]
    public async Task FutureVersion_IsNotLive_UntilWindowOpens()
    {
        await TruncateAsync();
        var n = await Repo().CreateNodeAsync(null, "p", "p", "P", "content", "u1");
        await Repo().AddVersionAsync(n.Id, "[]", "", Now.AddMinutes(-5), null, "u1"); // v1 live
        await Repo().AddVersionAsync(n.Id, "[]", "", Now.AddDays(1), null, "u1");      // v2 future

        var liveNow = await Repo().GetLiveVersionAsync(n.Id, Now);
        Assert.Equal(1, liveNow!.VersionId);

        await Repo().RecomputeCurrentAsync(n.Id, Now.AddDays(2));
        var later = await Repo().GetLiveVersionAsync(n.Id, Now.AddDays(2));
        Assert.Equal(2, later!.VersionId);
    }

    [Fact]
    public async Task SoftDelete_HidesFromTree_ButKeepsChildrenGuard()
    {
        await TruncateAsync();
        var support = await Repo().CreateNodeAsync(null, "support", "support", "Support", "folder", "u1");
        await Repo().CreateNodeAsync(support.Id, "faq", "support/faq", "FAQ", "content", "u1");

        Assert.True(await Repo().HasChildrenAsync(support.Id));

        var faq = await Repo().GetByPathAsync("support/faq");
        await Repo().SoftDeleteAsync(faq!.Id, "u1");
        Assert.Null(await Repo().GetByPathAsync("support/faq"));
    }
}
