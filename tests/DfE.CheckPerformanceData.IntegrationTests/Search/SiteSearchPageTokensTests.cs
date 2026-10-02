using DfE.CheckPerformanceData.Application.Common;
using DfE.CheckPerformanceData.Application.ContentBlocks;
using DfE.CheckPerformanceData.Application.Search;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Npgsql;

namespace DfE.CheckPerformanceData.IntegrationTests.Search;

// A search can name its pages by token (?pages=). Against a real database: the tokens are looked
// up to the pages that exist now, the search covers those pages and everything beneath them,
// and a page found by token is still found after it is renamed.
[Collection(nameof(PostgresCollection))]
public sealed class SiteSearchPageTokensTests(PostgresFixture fixture)
{
    private readonly PostgresFixture _fixture = fixture;

    private async Task TruncateAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"TRUNCATE ""ContentBlockVersions"", ""ContentBlocks"", ""PageNodeVersions"", ""PageNodes"" RESTART IDENTITY CASCADE;";
        await cmd.ExecuteNonQueryAsync();
    }

    private SiteSearchService BuildSut()
    {
        var ctx = _fixture.CreateContext();
        var pageRepo = new PageNodeRepository(ctx);
        var htmlRender = Substitute.For<IHtmlRenderingService>();
        htmlRender.RenderHtml(Arg.Any<string?>()).Returns(ci => ci.Arg<string?>());
        htmlRender.StripTagsToPlainText(Arg.Any<string?>()).Returns(ci => ci.Arg<string?>() ?? string.Empty);
        var blockSearch = new ContentBlockSearchService(new ContentBlockRepository(ctx), pageRepo, htmlRender);
        return new SiteSearchService(pageRepo, blockSearch, new FakeSearchTelemetry(), new SearchResultCanonicaliser(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SiteSearchService>.Instance);
    }

    private async Task<Guid> SeedPageAsync(string path, DateTime? deletedDate = null)
    {
        var now = DateTime.UtcNow;
        var page = new PageNode
        {
            Id = Guid.NewGuid(),
            Segment = path.Split('/').Last(),
            Path = path,
            Title = $"Widget {path}",
            Keywords = "widget",
            AppearInSearch = true,
            PageType = "content",
            DeletedDate = deletedDate,
            CreatedDate = now,
            UpdatedDate = now,
        };
        await using var ctx = _fixture.CreateContext();
        ctx.PageNodes.Add(page);
        ctx.PageNodeVersions.Add(new PageNodeVersion
        {
            Id = Guid.NewGuid(),
            PageNodeId = page.Id,
            VersionId = 1,
            IsCurrent = true,
            Content = "[]",
            BodyPlainText = "widget body",
            CreatedDate = now,
            UpdatedDate = now,
        });
        await ctx.SaveChangesAsync();
        return page.Id;
    }

    [Fact]
    [Trait("search-case", "scope-filter")]
    public async Task SearchAsync_ByPageTokens_FindsThosePagesAndEverythingBeneathThemOnly()
    {
        await TruncateAsync();
        var groupA = await SeedPageAsync("grp/a");
        await SeedPageAsync("grp/a/child");
        await SeedPageAsync("grp/a-sibling");
        var groupB = await SeedPageAsync("grp/b");
        await SeedPageAsync("other");

        var result = await BuildSut().SearchAsync(new SiteSearchQuery(
            Query: "widget", PageTokens: $"{PageToken.For(groupA)},{PageToken.For(groupB)}"));

        Assert.Equal(["/grp/a", "/grp/a/child", "/grp/b"], result.Hits.Select(h => h.Url).OrderBy(u => u).ToList());
        Assert.Equal("grp/a,grp/b", result.ScopePath);
    }

    [Fact]
    [Trait("search-case", "scope-filter")]
    public async Task SearchAsync_ByPageToken_StillFindsThePageAfterItIsRenamed()
    {
        await TruncateAsync();
        var page = await SeedPageAsync("grp/old-name");
        await SeedPageAsync("other");
        var token = PageToken.For(page);

        await using (var ctx = _fixture.CreateContext())
        {
            await ctx.PageNodes.Where(n => n.Id == page)
                .ExecuteUpdateAsync(u => u.SetProperty(n => n.Path, "grp/new-name").SetProperty(n => n.Segment, "new-name"));
        }

        var result = await BuildSut().SearchAsync(new SiteSearchQuery(Query: "widget", PageTokens: token));

        Assert.Equal(["/grp/new-name"], result.Hits.Select(h => h.Url).ToList());
    }

    // A deleted page's token names nothing. With no other page named, the search finds nothing
    // rather than widening to the whole site.
    [Fact]
    [Trait("search-case", "scope-filter")]
    public async Task SearchAsync_ByTheTokenOfADeletedPage_FindsNothing()
    {
        await TruncateAsync();
        var gone = await SeedPageAsync("grp/gone", deletedDate: DateTime.UtcNow);
        await SeedPageAsync("other");

        var result = await BuildSut().SearchAsync(new SiteSearchQuery(Query: "widget", PageTokens: PageToken.For(gone)));

        Assert.Empty(result.Hits);
    }

    // An existing ?scope= link is unaffected.
    [Fact]
    [Trait("search-case", "scope-filter")]
    public async Task SearchAsync_ByPathScope_WorksAsBefore()
    {
        await TruncateAsync();
        await SeedPageAsync("grp/a");
        await SeedPageAsync("grp/a/child");
        await SeedPageAsync("other");

        var result = await BuildSut().SearchAsync(new SiteSearchQuery(Query: "widget", ScopePath: "/grp/a/"));

        Assert.Equal(["/grp/a", "/grp/a/child"], result.Hits.Select(h => h.Url).OrderBy(u => u).ToList());
    }
}
