using DfE.CheckPerformanceData.Application.PageTree;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.IntegrationTests.RetriedTransactions;

// #757: each page tree delegate finds a node with FindAsync or a tracked query. After a failed
// commit the tracker still holds the new values. A retry then sees no change, and the update is
// lost.
[Collection(nameof(PostgresCollection))]
public sealed class PageNodeRetryTests(PostgresFixture fixture)
{
    private readonly PostgresFixture _fixture = fixture;

    private async Task ResetAsync()
    {
        await using var ctx = _fixture.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync(
            @"TRUNCATE ""PageNodes"", ""PageNodeVersions"", ""AuditEntries"" RESTART IDENTITY CASCADE;");
    }

    private async Task<PageNodeDto> SeedNodeAsync(Guid? parentId, string segment, string path)
    {
        await using var ctx = _fixture.CreateContext();
        return await new PageNodeRepository(ctx).CreateNodeAsync(parentId, segment, path, segment, "content", "seed");
    }

    [Fact]
    public async Task Sibling_order_that_is_retried_after_a_failed_commit_is_saved()
    {
        await ResetAsync();
        var a = await SeedNodeAsync(null, "a", "a");
        var b = await SeedNodeAsync(null, "b", "b");
        var interceptor = new FailFirstCommitInterceptor();
        await using var ctx = _fixture.CreateRetryingContext(interceptor);

        await new PageNodeRepository(ctx).SetSiblingOrderAsync([(a.Id, 5), (b.Id, 4)]);

        Assert.Equal(2, interceptor.Commits);
        await using var verify = _fixture.CreateContext();
        Assert.Equal(5, (await verify.PageNodes.SingleAsync(n => n.Id == a.Id)).SortOrder);
        Assert.Equal(4, (await verify.PageNodes.SingleAsync(n => n.Id == b.Id)).SortOrder);
    }

    [Fact]
    public async Task Rename_that_is_retried_after_a_failed_commit_is_saved()
    {
        await ResetAsync();
        var parent = await SeedNodeAsync(null, "old", "old");
        var child = await SeedNodeAsync(parent.Id, "child", "old/child");
        var interceptor = new FailFirstCommitInterceptor();
        await using var ctx = _fixture.CreateRetryingContext(interceptor);

        await new PageNodeRepository(ctx).RenameNodeAndCascadeAsync(
            parent.Id, "renamed", "renamed", "Renamed", null, null, "tester");

        Assert.Equal(2, interceptor.Commits);
        await using var verify = _fixture.CreateContext();
        var savedParent = await verify.PageNodes.SingleAsync(n => n.Id == parent.Id);
        Assert.Equal("renamed", savedParent.Segment);
        Assert.Equal("renamed", savedParent.Path);
        Assert.Equal("renamed/child", (await verify.PageNodes.SingleAsync(n => n.Id == child.Id)).Path);
    }

    [Fact]
    public async Task Move_that_is_retried_after_a_failed_commit_is_saved()
    {
        await ResetAsync();
        var from = await SeedNodeAsync(null, "from", "from");
        var to = await SeedNodeAsync(null, "to", "to");
        var node = await SeedNodeAsync(from.Id, "n", "from/n");
        var interceptor = new FailFirstCommitInterceptor();
        await using var ctx = _fixture.CreateRetryingContext(interceptor);

        await new PageNodeRepository(ctx).MoveNodeAsync(node.Id, to.Id, "to/n", 3, "tester");

        Assert.Equal(2, interceptor.Commits);
        await using var verify = _fixture.CreateContext();
        var saved = await verify.PageNodes.SingleAsync(n => n.Id == node.Id);
        Assert.Equal(to.Id, saved.ParentId);
        Assert.Equal("to/n", saved.Path);
        Assert.Equal(3, saved.SortOrder);
    }

    [Fact]
    public async Task Publish_that_is_retried_after_a_failed_commit_is_saved()
    {
        await ResetAsync();
        var node = await SeedNodeAsync(null, "page", "page");
        int versionId;
        await using (var seed = _fixture.CreateContext())
        {
            versionId = await new PageNodeRepository(seed).AddVersionAsync(
                node.Id, "{\"kind\":\"root\"}", "text", null, null, "seed");
        }

        var interceptor = new FailFirstCommitInterceptor();
        await using var ctx = _fixture.CreateRetryingContext(interceptor);
        var from = DateTime.UtcNow.AddDays(-1);

        await new PageNodeService(new PageNodeRepository(ctx)).PublishAsync(node.Id, versionId, from, null, "tester");

        Assert.Equal(2, interceptor.Commits);
        await using var verify = _fixture.CreateContext();
        var saved = await verify.PageNodeVersions.SingleAsync(v => v.PageNodeId == node.Id && v.VersionId == versionId);
        Assert.NotNull(saved.PublishFrom);
    }
}
