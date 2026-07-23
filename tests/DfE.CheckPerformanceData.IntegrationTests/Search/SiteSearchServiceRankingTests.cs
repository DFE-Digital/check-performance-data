using DfE.CheckPerformanceData.Application.Common;
using DfE.CheckPerformanceData.Application.ContentBlocks;
using DfE.CheckPerformanceData.Application.Search;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Repositories;
using NSubstitute;
using Npgsql;

namespace DfE.CheckPerformanceData.IntegrationTests.Search;

// Service-tier ranking regression fixture — pins the contract that a PageNode with the
// query term only in Keywords outranks a PageNode with the same term only in Body.
// Keywords is weight A on PageNode.SearchVector; Body is weight D on PageNodeVersion.SearchVector.
// The combined ts_rank sum for a Keywords-only hit must exceed a Body-only hit; if the weight
// table is edited to invert this ordering the test fails.
//
// Relative-ordering assertions only — absolute ts_rank floats are corpus-dependent and must
// never be asserted.
[Collection(nameof(PostgresCollection))]
public sealed class SiteSearchServiceRankingTests(PostgresFixture fixture)
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
        var blockRepo = new ContentBlockRepository(ctx);
        var htmlRender = Substitute.For<IHtmlRenderingService>();
        htmlRender.RenderHtml(Arg.Any<string?>()).Returns(ci => ci.Arg<string?>());
        htmlRender.StripTagsToPlainText(Arg.Any<string?>()).Returns(ci => ci.Arg<string?>() ?? string.Empty);
        var blockSearch = new ContentBlockSearchService(blockRepo, pageRepo, htmlRender);
        return new SiteSearchService(pageRepo, blockSearch, new FakeSearchTelemetry());
    }

    private static PageNode BuildPage(string slug, string title, string? keywords = null)
    {
        var now = DateTime.UtcNow;
        return new PageNode
        {
            Id = Guid.NewGuid(),
            Segment = slug,
            Path = slug,
            Title = title,
            Keywords = keywords,
            AppearInSearch = true,
            PageType = "content",
            CreatedDate = now,
            UpdatedDate = now,
        };
    }

    private static PageNodeVersion BuildLiveVersion(Guid pageId, string bodyPlainText)
    {
        var now = DateTime.UtcNow;
        return new PageNodeVersion
        {
            Id = Guid.NewGuid(),
            PageNodeId = pageId,
            VersionId = 1,
            MinorVersion = 0,
            IsCurrent = true,
            Content = "[]",
            BodyPlainText = bodyPlainText,
            CreatedDate = now,
            UpdatedDate = now,
        };
    }

    private async Task SeedAsync(params (PageNode Page, string Body)[] rows)
    {
        await using var ctx = _fixture.CreateContext();
        foreach (var (page, body) in rows)
        {
            ctx.PageNodes.Add(page);
            ctx.PageNodeVersions.Add(BuildLiveVersion(page.Id, body));
        }
        await ctx.SaveChangesAsync();
    }

    [Fact]
    [Trait("search-case", "keywords-boost")]
    public async Task SearchAsync_PageWithWidgetInKeywordsOnly_OutranksPageWithWidgetInBodyOnly()
    {
        await TruncateAsync();
        // Page A: Keywords hit only (weight A on PageNode.SearchVector); blank body so the
        // PageNodeVersion vector holds no "widget" lexeme.
        var pageA = BuildPage("alpha-page", "Alpha page", keywords: "widget");
        // Page B: Body hit only (weight D on PageNodeVersion.SearchVector); no keywords so
        // the PageNode vector holds no "widget" lexeme.
        var pageB = BuildPage("beta-page", "Beta page");
        await SeedAsync((pageA, string.Empty), (pageB, "widget content here"));

        var result = await BuildSut().SearchAsync(new SiteSearchQuery(Query: "widget"));

        Assert.Equal(2, result.PageHits.Count);
        Assert.Equal(pageA.Id, result.PageHits[0].PageId);
        Assert.Equal(pageB.Id, result.PageHits[1].PageId);
        // Relative comparison only — never assert absolute ts_rank floats.
        Assert.True(result.PageHits[0].Rank > result.PageHits[1].Rank);
    }
}
