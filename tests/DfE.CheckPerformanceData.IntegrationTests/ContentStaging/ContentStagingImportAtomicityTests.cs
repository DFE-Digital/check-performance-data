using DfE.CheckPerformanceData.Application.Common;
using DfE.CheckPerformanceData.Application.ContentStaging;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.IntegrationTests.ContentStaging;

// A page and its versions have to land together. Written separately, a version write that fails
// leaves the node committed with nothing to render — a permanent 404 sitting in the page tree,
// which a later export then carries onward — while the summary reports that nothing was created.
//
// The failures used here are the ones a real bundle can carry: a duplicate VersionId (rejected by
// the unique index on (PageNodeId, VersionId)) and a raw NUL byte (which Postgres refuses in a
// text column). Both are per-item errors by design; the question is what state they leave behind.
[Collection(nameof(PostgresCollection))]
public sealed class ContentStagingImportAtomicityTests(PostgresFixture fixture)
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

    private static ContentBundle BundleWith(params PageNodeVersionBundleItem[] versions) =>
        new()
        {
            PageNodes =
            [
                new()
                {
                    Id = Guid.NewGuid(),
                    Segment = "doomed",
                    Title = "Doomed",
                    PageType = "content",
                    Versions = versions.ToList()
                }
            ]
        };

    public static TheoryData<string, ContentBundle> FailingBundles() => new()
    {
        {
            "duplicate version ids",
            BundleWith(
                new PageNodeVersionBundleItem { VersionId = 1, Content = "{\"kind\":\"root\"}" },
                new PageNodeVersionBundleItem { VersionId = 1, Content = "{\"kind\":\"root\"}" })
        },
        {
            "a raw NUL byte in the body",
            BundleWith(new PageNodeVersionBundleItem { VersionId = 1, Content = "{\"kind\":\"root\",\"x\":\"\0\"}" })
        },
    };

    [Theory]
    [MemberData(nameof(FailingBundles))]
    public async Task Import_WhenTheVersionWriteFails_LeavesNoPageBehind(string shape, ContentBundle bundle)
    {
        await ResetAsync();

        ContentImportResult result;
        {
            var staging = NewStaging(out var ctx);
            await using (ctx) result = await staging.ImportAsync(bundle, ContentImportMode.Replace);
        }

        await using var after = _fixture.CreateContext();

        // The error is expected — this is about what it leaves behind.
        Assert.NotEmpty(result.Errors);
        Assert.Equal(0, result.PageNodesCreated);
        Assert.False(
            await after.PageNodes.AnyAsync(p => p.Segment == "doomed"),
            $"[{shape}] the page was rolled back from the summary but left in the tree");
    }

    // The counterpart: a page that imports cleanly is fully there, versions and all.
    [Fact]
    public async Task Import_WhenTheVersionWriteSucceeds_CommitsBothTheNodeAndItsVersions()
    {
        await ResetAsync();
        var bundle = BundleWith(
            new PageNodeVersionBundleItem
            {
                VersionId = 1,
                Content = "{\"kind\":\"root\",\"children\":[]}",
                PublishFrom = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            });

        ContentImportResult result;
        {
            var staging = NewStaging(out var ctx);
            await using (ctx) result = await staging.ImportAsync(bundle, ContentImportMode.Replace);
        }

        await using var after = _fixture.CreateContext();
        var page = await after.PageNodes.SingleAsync(p => p.Segment == "doomed");

        Assert.Empty(result.Errors);
        Assert.Equal(1, result.PageNodesCreated);
        Assert.Equal(1, await after.PageNodeVersions.CountAsync(v => v.PageNodeId == page.Id));
    }

    // A non-folder page carrying no versions cannot come from the CMS — creating a content or
    // wiki page always writes an initial version — so it is a hand-edited or faulty bundle. It
    // still imports, but the operator is told, because the page will never render.
    [Fact]
    public async Task Import_PageWithNoVersions_Warns()
    {
        await ResetAsync();
        var bundle = BundleWith();

        ContentImportResult result;
        {
            var staging = NewStaging(out var ctx);
            await using (ctx) result = await staging.ImportAsync(bundle, ContentImportMode.Replace);
        }

        Assert.Contains(result.Warnings, w => w.Contains("no versions", StringComparison.OrdinalIgnoreCase));
    }
}
