using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DfE.CheckPerformanceData.IntegrationTests.PageTree;

[Collection(nameof(PostgresCollection))]
public sealed class PageNodePersistenceTests(PostgresFixture fixture)
{
    private readonly PostgresFixture _fixture = fixture;

    private async Task TruncateAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"TRUNCATE ""PageNodeVersions"", ""PageNodes"" RESTART IDENTITY CASCADE;";
        await cmd.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Inserts_And_Reads_Back_A_Node_WithGuidKey()
    {
        await TruncateAsync();
        var id = Guid.NewGuid();
        await using (var ctx = _fixture.CreateContext())
        {
            ctx.PageNodes.Add(new PageNode
            {
                Id = id, ParentId = null, Segment = "support", Path = "support",
                SortOrder = 0, Title = "Support", PageType = "folder",
                CreatedDate = DateTime.UtcNow, UpdatedDate = DateTime.UtcNow
            });
            await ctx.SaveChangesAsync();
        }
        await using var read = _fixture.CreateContext();
        var node = await read.PageNodes.SingleAsync(n => n.Id == id);
        Assert.Equal("support", node.Path);
        Assert.Equal("folder", node.PageType);
    }

    [Fact]
    public async Task Path_IsUnique_WhereNotDeleted()
    {
        await TruncateAsync();
        await using var ctx = _fixture.CreateContext();
        ctx.PageNodes.Add(new PageNode { Id = Guid.NewGuid(), Segment = "a", Path = "a", Title = "A", PageType = "folder", CreatedDate = DateTime.UtcNow, UpdatedDate = DateTime.UtcNow });
        await ctx.SaveChangesAsync();
        ctx.PageNodes.Add(new PageNode { Id = Guid.NewGuid(), Segment = "a", Path = "a", Title = "A2", PageType = "folder", CreatedDate = DateTime.UtcNow, UpdatedDate = DateTime.UtcNow });
        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }
}
