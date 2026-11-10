using DfE.CheckPerformanceData.Application.Common;
using DfE.CheckPerformanceData.Application.ContentBlocks;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.IntegrationTests.RetriedTransactions;

// #757: the execution strategy runs a transaction delegate again after a transient fault, with the
// same context. The content block repository wrapper must start each run with a clean change tracker.
[Collection(nameof(PostgresCollection))]
public sealed class ContentBlockRetryTests(PostgresFixture fixture)
{
    private readonly PostgresFixture _fixture = fixture;

    private async Task ResetAsync()
    {
        await using var ctx = _fixture.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync(
            @"TRUNCATE ""ContentBlocks"", ""ContentBlockVersions"", ""AuditEntries"" RESTART IDENTITY CASCADE;");
    }

    private static ContentBlockService NewService(PortalDbContext ctx) =>
        new(new ContentBlockRepository(ctx), new HtmlRenderingService());

    [Fact]
    public async Task Ensure_that_is_retried_after_a_transient_fault_creates_one_block_and_one_version()
    {
        await ResetAsync();
        var interceptor = new FailFirstCommandInterceptor("INSERT INTO \"ContentBlockVersions\"");
        await using var ctx = _fixture.CreateRetryingContext(interceptor);

        var block = await NewService(ctx).EnsureAsync("retry-key", "Content", "<p>Default</p>", "/some-page");

        Assert.Equal(2, interceptor.Matches);
        await using var verify = _fixture.CreateContext();
        var row = await verify.ContentBlocks.SingleAsync(b => b.Key == "retry-key");
        Assert.Equal(block.Id, row.Id);
        Assert.Equal("/some-page", row.LastSeenPath);
        var version = await verify.ContentBlockVersions.SingleAsync(v => v.ContentBlockId == row.Id);
        Assert.Equal(1, version.VersionNumber);
        Assert.Equal(1, await verify.AuditEntries.CountAsync(a =>
            a.EntityType == nameof(ContentBlock) && a.Action == "Insert"));
    }

    [Fact]
    public async Task Save_that_is_retried_after_a_failed_commit_keeps_the_new_value()
    {
        await ResetAsync();
        await using (var seed = _fixture.CreateContext())
        {
            await NewService(seed).SaveAsync(new SaveContentBlockDto
            {
                Key = "save-key", BlockType = "Content", Value = "<p>Old</p>"
            });
        }

        var interceptor = new FailFirstCommitInterceptor();
        await using var ctx = _fixture.CreateRetryingContext(interceptor);

        await NewService(ctx).SaveAsync(new SaveContentBlockDto
        {
            Key = "save-key", BlockType = "Content", Value = "<p>New</p>"
        });

        Assert.Equal(2, interceptor.Commits);
        await using var verify = _fixture.CreateContext();
        var row = await verify.ContentBlocks.SingleAsync(b => b.Key == "save-key");
        Assert.Equal("<p>New</p>", row.Value);
        var versions = await verify.ContentBlockVersions
            .Where(v => v.ContentBlockId == row.Id).OrderBy(v => v.VersionNumber).ToListAsync();
        Assert.Equal([1, 2], versions.Select(v => v.VersionNumber));
        Assert.Equal("<p>New</p>", versions[1].Value);
    }
}
