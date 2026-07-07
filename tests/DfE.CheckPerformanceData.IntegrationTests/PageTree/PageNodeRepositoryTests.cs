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
    public async Task UpdateVersionWindow_WritesPublishFromAndPublishTo()
    {
        await TruncateAsync();
        var n = await Repo().CreateNodeAsync(null, "p", "p", "P", "content", "u1");
        var versionId = await Repo().AddVersionAsync(n.Id, "[]", "", null, null, "u1");

        var from = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        var to   = new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Utc);

        await Repo().UpdateVersionWindowAsync(n.Id, versionId, from, to, "publisher-1");

        var versions = await Repo().GetVersionsAsync(n.Id);
        Assert.Single(versions);
        Assert.Equal(from, versions[0].PublishFrom);
        Assert.Equal(to, versions[0].PublishTo);
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

    [Fact]
    public async Task SetSiblingOrderAsync_PersistsNewOrdersForAllSiblings()
    {
        await TruncateAsync();

        var parent = await Repo().CreateNodeAsync(null, "parent", "parent", "Parent", "folder", "u1");
        var nodeA  = await Repo().CreateNodeAsync(parent.Id, "a", "parent/a", "A", "content", "u1");
        var nodeB  = await Repo().CreateNodeAsync(parent.Id, "b", "parent/b", "B", "content", "u1");
        var nodeC  = await Repo().CreateNodeAsync(parent.Id, "c", "parent/c", "C", "content", "u1");

        // Reverse the order: A→2, B→1, C→0.
        await Repo().SetSiblingOrderAsync(new[]
        {
            (nodeA.Id, 2),
            (nodeB.Id, 1),
            (nodeC.Id, 0)
        });

        var tree = await Repo().GetTreeAsync();
        var siblings = tree.Where(n => n.ParentId == parent.Id)
            .OrderBy(n => n.SortOrder).ThenBy(n => n.CreatedDate)
            .ToList();

        Assert.Equal(nodeC.Id, siblings[0].Id);
        Assert.Equal(nodeB.Id, siblings[1].Id);
        Assert.Equal(nodeA.Id, siblings[2].Id);
    }

    // ── MinorVersion lifecycle ───────────────────────────────────────────────

    [Fact]
    public async Task AddVersion_Sets_MinorVersion_To_1()
    {
        await TruncateAsync();
        var n = await Repo().CreateNodeAsync(null, "p", "p", "P", "content", "u1");

        var versionId = await Repo().AddVersionAsync(n.Id, "[]", "", null, null, "u1");

        var versions = await Repo().GetVersionsAsync(n.Id);
        Assert.Single(versions);
        Assert.Equal(versionId, versions[0].VersionId);
        Assert.Equal(1, versions[0].MinorVersion);
    }

    [Fact]
    public async Task UpdateVersionContent_Increments_MinorVersion()
    {
        await TruncateAsync();
        var n = await Repo().CreateNodeAsync(null, "p", "p", "P", "content", "u1");
        var versionId = await Repo().AddVersionAsync(n.Id, "[]", "", null, null, "u1");
        // MinorVersion starts at 1; each content save bumps it.

        await Repo().UpdateVersionContentAsync(n.Id, versionId, "[updated]", "updated", "u1");

        var versions = await Repo().GetVersionsAsync(n.Id);
        Assert.Equal(2, versions[0].MinorVersion);

        await Repo().UpdateVersionContentAsync(n.Id, versionId, "[updated2]", "updated2", "u1");
        versions = await Repo().GetVersionsAsync(n.Id);
        Assert.Equal(3, versions[0].MinorVersion);
    }

    [Fact]
    public async Task UpdateVersionWindow_WithPublishFrom_Sets_MinorVersion_To_0()
    {
        await TruncateAsync();
        var n = await Repo().CreateNodeAsync(null, "p", "p", "P", "content", "u1");
        var versionId = await Repo().AddVersionAsync(n.Id, "[]", "", null, null, "u1");
        // Verify it starts at 1.
        var before = await Repo().GetVersionsAsync(n.Id);
        Assert.Equal(1, before[0].MinorVersion);

        var publishFrom = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        await Repo().UpdateVersionWindowAsync(n.Id, versionId, publishFrom, null, "u1");

        var after = await Repo().GetVersionsAsync(n.Id);
        Assert.Equal(0, after[0].MinorVersion);
    }

    [Fact]
    public async Task UpdateVersionWindow_WithNullPublishFrom_Leaves_MinorVersion_Unchanged()
    {
        await TruncateAsync();
        var n = await Repo().CreateNodeAsync(null, "p", "p", "P", "content", "u1");
        var versionId = await Repo().AddVersionAsync(n.Id, "[]", "", null, null, "u1");
        // Save content twice to bump minor to 3.
        await Repo().UpdateVersionContentAsync(n.Id, versionId, "[v2]", "", "u1");
        await Repo().UpdateVersionContentAsync(n.Id, versionId, "[v3]", "", "u1");
        var mid = await Repo().GetVersionsAsync(n.Id);
        Assert.Equal(3, mid[0].MinorVersion);

        // Unpublish (publishFrom = null) must not change minor.
        await Repo().UpdateVersionWindowAsync(n.Id, versionId, null, null, "u1");

        var after = await Repo().GetVersionsAsync(n.Id);
        Assert.Equal(3, after[0].MinorVersion);
    }

    [Fact]
    public async Task SwapSortOrder_ExchangesSortOrderOfTwoSiblings()
    {
        await TruncateAsync();

        var parent = await Repo().CreateNodeAsync(null, "parent", "parent", "Parent", "folder", "u1");
        var nodeA   = await Repo().CreateNodeAsync(parent.Id, "a", "parent/a", "A", "content", "u1");
        var nodeB   = await Repo().CreateNodeAsync(parent.Id, "b", "parent/b", "B", "content", "u1");

        // Give the siblings distinct SortOrder values via direct SQL so the swap is observable.
        await using (var conn = new NpgsqlConnection(_fixture.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd1 = conn.CreateCommand();
            cmd1.CommandText = @"UPDATE ""PageNodes"" SET ""SortOrder"" = 10 WHERE ""Id"" = @id";
            cmd1.Parameters.AddWithValue("id", nodeA.Id);
            await cmd1.ExecuteNonQueryAsync();

            await using var cmd2 = conn.CreateCommand();
            cmd2.CommandText = @"UPDATE ""PageNodes"" SET ""SortOrder"" = 20 WHERE ""Id"" = @id";
            cmd2.Parameters.AddWithValue("id", nodeB.Id);
            await cmd2.ExecuteNonQueryAsync();
        }

        await Repo().SwapSortOrderAsync(nodeA.Id, nodeB.Id);

        // Verify via GetTreeAsync: A should now have 20, B should have 10.
        var tree = await Repo().GetTreeAsync();
        var rowA = tree.First(n => n.Id == nodeA.Id);
        var rowB = tree.First(n => n.Id == nodeB.Id);
        Assert.Equal(20, rowA.SortOrder);
        Assert.Equal(10, rowB.SortOrder);
    }
}
