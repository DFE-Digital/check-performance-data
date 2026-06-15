using DfE.CheckPerformance.Persistence.Entities;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.IntegrationTests.Wiki;

[Collection(nameof(PostgresCollection))]
public sealed class WikiRepositoryHardDeleteTests(PostgresFixture fixture)
{
    private readonly PostgresFixture _fixture = fixture;

    private WikiRepository CreateRepo(out PortalDbContext ctx)
    {
        ctx = _fixture.CreateContext();
        return new WikiRepository(ctx, new FakeCurrentUserService());
    }

    private static WikiPage NewPage(string title, string slug, int? parentId = null, bool isDeleted = true)
    {
        var now = DateTime.UtcNow;
        return new WikiPage
        {
            Title = title,
            Slug = slug,
            Content = $"<p>{title}</p>",
            BodyPlainText = title,
            ParentId = parentId,
            CreatedAt = now,
            UpdatedAt = now,
            IsDeleted = isDeleted,
            DeletedAt = isDeleted ? now : null,
            DeletedBy = isDeleted ? "seed-user" : null
        };
    }

    private static WikiPageVersion NewVersion(int wikiPageId, string title, int versionNumber)
    {
        return new WikiPageVersion
        {
            WikiPageId = wikiPageId,
            Title = title,
            Content = $"<p>{title} v{versionNumber}</p>",
            VersionNumber = versionNumber,
            CreatedAt = DateTime.UtcNow
        };
    }

    // --- HardDeleteAsync_RemovesRootPlusDescendantsPlusVersionsInOneTransaction ---

    [Fact]
    public async Task HardDeleteAsync_RemovesRootPlusDescendantsPlusVersionsInOneTransaction()
    {
        await _fixture.ResetAsync();
        var repo = CreateRepo(out var ctx);
        await using var _ = ctx;

        var parent = NewPage("Parent", "parent");
        ctx.WikiPages.Add(parent);
        await ctx.SaveChangesAsync();

        var childA = NewPage("Child A", "child-a", parentId: parent.Id);
        var childB = NewPage("Child B", "child-b", parentId: parent.Id);
        ctx.WikiPages.AddRange(childA, childB);
        await ctx.SaveChangesAsync();

        ctx.WikiPageVersions.Add(NewVersion(parent.Id, "Parent", 1));
        ctx.WikiPageVersions.Add(NewVersion(childA.Id, "Child A", 1));
        ctx.WikiPageVersions.Add(NewVersion(childB.Id, "Child B", 1));
        await ctx.SaveChangesAsync();

        var rootId = parent.Id;
        var childAId = childA.Id;
        var childBId = childB.Id;

        await ctx.ExecuteInTransactionAsync(() => repo.HardDeleteAsync(rootId));

        // Verify through a fresh context — entities are detached.
        await using var verify = _fixture.CreateContext();
        var remainingPages = await verify.WikiPages
            .IgnoreQueryFilters()
            .Where(p => p.Id == rootId || p.Id == childAId || p.Id == childBId)
            .CountAsync();
        Assert.Equal(0, remainingPages);

        var remainingVersions = await verify.WikiPageVersions
            .Where(v => v.WikiPageId == rootId || v.WikiPageId == childAId || v.WikiPageId == childBId)
            .CountAsync();
        Assert.Equal(0, remainingVersions);
    }

    // --- HardDeleteAsync_WritesOneAuditEntryPerDeletedPage_With_Action_Delete ---

    [Fact]
    public async Task HardDeleteAsync_WritesOneAuditEntryPerDeletedPage_With_Action_Delete()
    {
        await _fixture.ResetAsync();
        var repo = CreateRepo(out var ctx);
        await using var _ = ctx;

        var parent = NewPage("Parent", "parent");
        ctx.WikiPages.Add(parent);
        await ctx.SaveChangesAsync();

        var childA = NewPage("Child A", "child-a", parentId: parent.Id);
        var childB = NewPage("Child B", "child-b", parentId: parent.Id);
        ctx.WikiPages.AddRange(childA, childB);
        await ctx.SaveChangesAsync();

        // Clear audit history from the seeding inserts so the assertion is unambiguous. The
        // audit table is now INSERT-only at the database (a BEFORE DELETE trigger raises), so
        // the test resets it with TRUNCATE, which is not a row-level delete and bypasses it.
        await using (var clean = _fixture.CreateContext())
        {
            await clean.Database.ExecuteSqlRawAsync(@"TRUNCATE ""AuditEntries"";");
        }

        var rootId = parent.Id;

        await ctx.ExecuteInTransactionAsync(() => repo.HardDeleteAsync(rootId));

        await using var verify = _fixture.CreateContext();
        var deleteAudits = await verify.AuditEntries
            .Where(a => a.EntityType == "WikiPage" && a.Action == "Delete")
            .ToListAsync();

        Assert.Equal(3, deleteAudits.Count);
        Assert.All(deleteAudits, a => Assert.Equal("test-user", a.UserId));
        Assert.All(deleteAudits, a => Assert.False(string.IsNullOrEmpty(a.OldValues)));
    }

    // --- HardDeleteAsync_ThrowsAndRollsBack_OnTransactionFailure ---

    [Fact]
    public async Task HardDeleteAsync_ThrowsAndRollsBack_OnTransactionFailure()
    {
        await _fixture.ResetAsync();
        var repo = CreateRepo(out var ctx);
        await using var _ = ctx;

        var parent = NewPage("Parent", "parent");
        ctx.WikiPages.Add(parent);
        await ctx.SaveChangesAsync();

        var child = NewPage("Child", "child", parentId: parent.Id);
        ctx.WikiPages.Add(child);
        await ctx.SaveChangesAsync();

        var rootId = parent.Id;

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await ctx.ExecuteInTransactionAsync(async () =>
            {
                await repo.HardDeleteAsync(rootId);
                throw new InvalidOperationException("simulated mid-transaction failure");
            });
        });

        // Rollback restored both rows.
        await using var verify = _fixture.CreateContext();
        var rows = await verify.WikiPages
            .IgnoreQueryFilters()
            .Where(p => p.Id == rootId || p.ParentId == rootId)
            .CountAsync();
        Assert.Equal(2, rows);
    }

    // --- HardDeleteAsync_NotFound_ThrowsInvalidOperationException ---

    [Fact]
    public async Task HardDeleteAsync_NotFound_ThrowsInvalidOperationException()
    {
        await _fixture.ResetAsync();
        var repo = CreateRepo(out var ctx);
        await using var _ = ctx;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => repo.HardDeleteAsync(int.MaxValue));
    }
}
