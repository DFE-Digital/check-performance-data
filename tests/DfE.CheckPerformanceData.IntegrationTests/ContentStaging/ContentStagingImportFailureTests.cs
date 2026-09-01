using DfE.CheckPerformanceData.Application.Common;
using DfE.CheckPerformanceData.Application.ContentStaging;
using DfE.CheckPerformanceData.Application.PageTree;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.IntegrationTests.ContentStaging;

// Pins the actual failure behaviour of ContentStagingService.ImportAsync against a real
// Postgres — separates "what fails cleanly and rolls back" from "what fails per-item and
// leaves partial state" so a future refactor can't quietly flip the contract.
//
// Current contract (as designed):
//   * Validator-fatal issues (bad segment, unknown PageType, too-large payload, etc.)
//     throw ContentImportValidationException BEFORE any mutation, so no partial commit
//     is possible — the caller sees an exception, the DB is untouched.
//   * Per-page runtime failures (unknown ParentId, seeder collision, etc.) are caught
//     inside the loop and added to result.Errors; the loop continues so surviving items
//     still get imported. This is best-effort-per-item, NOT all-or-nothing.
//
// If the atomicity policy ever needs to change (all-or-nothing outer transaction), the
// per-item tests here fail as the contract update — which is the right signal.
[Collection(nameof(PostgresCollection))]
public sealed class ContentStagingImportFailureTests(PostgresFixture fixture)
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
        var pageRepo = new PageNodeRepository(ctx);
        var blockRepo = new ContentBlockRepository(ctx);
        return new ContentStagingService(pageRepo, blockRepo, new HtmlRenderingService());
    }

    // Validator-fatal issue → whole-bundle rejection, zero writes.
    [Fact]
    public async Task Import_ValidatorFatalIssue_ThrowsAndCommitsNothing()
    {
        await ResetAsync();

        var bundle = new ContentBundle
        {
            PageNodes =
            {
                new() { Id = Guid.NewGuid(), Segment = "help",     Title = "Help",     PageType = "folder" },
                new() { Id = Guid.NewGuid(), Segment = "faq",      Title = "FAQ",      PageType = "content" },
                // Uppercase segment fails SEGMENT_INVALID → whole bundle rejected pre-mutation.
                new() { Id = Guid.NewGuid(), Segment = "BROKEN",   Title = "Broken",   PageType = "content" },
            }
        };

        var staging = NewStaging(out var ctx);
        await using var _ = ctx;

        await Assert.ThrowsAsync<ContentImportValidationException>(async () =>
            await staging.ImportAsync(bundle, ContentImportMode.Fail));

        // No mutation should have happened — the validator throws BEFORE the import loop.
        await using var check = _fixture.CreateContext();
        Assert.Equal(0, await check.PageNodes.CountAsync());
    }

    // Per-item runtime failure (unknown ParentId) → the failing item is skipped with an
    // error entry, siblings that don't share the failure are still imported. Documents the
    // best-effort-per-item contract.
    [Fact]
    public async Task Import_UnknownParentId_SkipsFailingPage_ButCommitsValidSiblings()
    {
        await ResetAsync();

        var rootA = Guid.NewGuid();
        var rootB = Guid.NewGuid();
        var bundle = new ContentBundle
        {
            PageNodes =
            {
                new() { Id = rootA, Segment = "support",  Title = "Support",  PageType = "folder" },
                new() { Id = rootB, Segment = "guidance", Title = "Guidance", PageType = "folder" },
                // ParentId points at a GUID that is neither in the bundle nor in the DB. The
                // service must add an entry to result.Errors and continue.
                new()
                {
                    Id = Guid.NewGuid(),
                    ParentId = Guid.NewGuid(),
                    Segment = "orphan",
                    Title = "Orphan",
                    PageType = "content",
                },
            }
        };

        var staging = NewStaging(out var ctx);
        await using var _ = ctx;

        var result = await staging.ImportAsync(bundle, ContentImportMode.Fail);

        // Two roots imported, one orphan rejected — result reflects the split.
        Assert.Equal(2, result.PageNodesCreated);
        Assert.Single(result.Errors);
        Assert.Contains("Orphan", result.Errors[0]);

        // DB shows the surviving roots but NOT the orphan.
        await using var check = _fixture.CreateContext();
        var paths = await check.PageNodes.Select(p => p.Path).ToListAsync();
        Assert.Contains("support", paths);
        Assert.Contains("guidance", paths);
        Assert.DoesNotContain("orphan", paths);
    }

    // Combination test: a bundle whose FIRST page succeeds and whose SECOND page fails
    // at runtime demonstrates that already-committed pages are NOT rolled back. Pins the
    // best-effort contract explicitly so a future refactor introducing an outer
    // transaction has to update this test as the contract change.
    [Fact]
    public async Task Import_PartialFailure_PriorSuccessesAreCommitted()
    {
        await ResetAsync();

        var committedRoot = Guid.NewGuid();
        var bundle = new ContentBundle
        {
            PageNodes =
            {
                // Valid — will be committed first.
                new() { Id = committedRoot, Segment = "committed", Title = "Committed", PageType = "folder" },
                // Invalid parent — per-item skip, does not roll back the committed row above.
                new()
                {
                    Id = Guid.NewGuid(),
                    ParentId = Guid.NewGuid(),
                    Segment = "orphan",
                    Title = "Orphan",
                    PageType = "content",
                },
            }
        };

        var staging = NewStaging(out var ctx);
        await using var _ = ctx;

        var result = await staging.ImportAsync(bundle, ContentImportMode.Fail);

        Assert.Equal(1, result.PageNodesCreated);
        Assert.Single(result.Errors);

        await using var check = _fixture.CreateContext();
        Assert.Equal(1, await check.PageNodes.CountAsync());
        Assert.Equal("committed", (await check.PageNodes.SingleAsync()).Path);
    }
}
