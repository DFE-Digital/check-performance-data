using DfE.CheckPerformanceData.Application.Common;
using DfE.CheckPerformanceData.Application.ContentBlocks;
using DfE.CheckPerformanceData.Application.Search;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Repositories;
using NSubstitute;
using Npgsql;

namespace DfE.CheckPerformanceData.IntegrationTests.Search;

// End-to-end proof that the canonicalisation contract holds against a live Postgres:
// multiple contributors on the same URL fold into one canonical hit; the aggregate rank
// is the MAX of contributor raw ts_ranks; and the merged list is ordered by aggregate
// rank descending. Composes the same service graph SearchTelemetryE2ETests uses so any
// regression in the assembly (SiteSearchService not calling the canonicaliser, the
// canonicaliser not folding URLs, the block service losing its scope filter) is caught
// at the outermost boundary.
//
// Method-level [Trait("search-case", "duplicate-blocks")] on every fact so the coverage
// meta-test can enumerate this dimension.
[Collection(nameof(PostgresCollection))]
public sealed class SearchCanonicalisationE2ETests(PostgresFixture fixture)
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

    // Compose the search graph exactly as production wires it. Zero-dep canonicaliser
    // is instantiated directly — no DI container needed.
    private SiteSearchService BuildSut()
    {
        var ctx = _fixture.CreateContext();
        var pageRepo = new PageNodeRepository(ctx);
        var blockRepo = new ContentBlockRepository(ctx);
        var htmlRender = Substitute.For<IHtmlRenderingService>();
        htmlRender.RenderHtml(Arg.Any<string?>()).Returns(ci => ci.Arg<string?>());
        htmlRender.StripTagsToPlainText(Arg.Any<string?>()).Returns(ci => ci.Arg<string?>() ?? string.Empty);
        var blockSearch = new ContentBlockSearchService(blockRepo, pageRepo, htmlRender);
        var canonicaliser = new SearchResultCanonicaliser();
        return new SiteSearchService(pageRepo, blockSearch, new FakeSearchTelemetry(), canonicaliser, Microsoft.Extensions.Logging.Abstractions.NullLogger<SiteSearchService>.Instance);
    }

    private static ContentBlock BuildBlock(
        string key,
        string keywords,
        string lastSeenPath,
        string valuePlainText = "widget copy",
        bool appearInSearch = true)
    {
        var now = DateTime.UtcNow;
        return new ContentBlock
        {
            Key = key,
            BlockType = "prose",
            Value = valuePlainText,
            ValuePlainText = valuePlainText,
            Keywords = keywords,
            AppearInSearch = appearInSearch,
            LastSeenPath = lastSeenPath,
            LastSeenAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    private static PageNode BuildPage(
        string slug,
        string title,
        string? keywords = null)
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

    private async Task SeedBlocksAsync(params ContentBlock[] blocks)
    {
        await using var ctx = _fixture.CreateContext();
        foreach (var b in blocks) ctx.ContentBlocks.Add(b);
        await ctx.SaveChangesAsync();
    }

    private async Task SeedPagesAsync(params (PageNode Page, string Body)[] rows)
    {
        await using var ctx = _fixture.CreateContext();
        foreach (var (page, body) in rows)
        {
            ctx.PageNodes.Add(page);
            ctx.PageNodeVersions.Add(BuildLiveVersion(page.Id, body));
        }
        await ctx.SaveChangesAsync();
    }

    // ── Two-blocks-same-url fold ─────────────────────────────────────────────

    [Fact]
    [Trait("search-case", "duplicate-blocks")]
    public async Task SearchAsync_TwoBlocksResolvingToSamePageUrl_FoldIntoOneCanonicalHit_WithMaxRank()
    {
        await TruncateAsync();

        // Both blocks live at /check-your-pupil-data — the block resolver picks up the
        // title from the static-route whitelist (no PageNode needed). Different keyword
        // repetition drives different ts_ranks so the MAX-scan is testable rather than
        // a coincidental tie.
        var strong = BuildBlock(
            "hero-widget-fold",
            keywords: "widgetfold widgetfold widgetfold",
            lastSeenPath: "/check-your-pupil-data",
            valuePlainText: "widgetfold appears prominently in the hero block");
        var weak = BuildBlock(
            "footer-widget-fold",
            keywords: "other-term",
            lastSeenPath: "/check-your-pupil-data",
            valuePlainText: "the footer briefly mentions widgetfold once");
        await SeedBlocksAsync(strong, weak);

        var result = await BuildSut().SearchAsync(new SiteSearchQuery(Query: "widgetfold"));

        var hit = Assert.Single(result.Hits);
        Assert.Equal("/check-your-pupil-data", hit.Url);
        Assert.Equal(0, hit.PageContributorCount);
        Assert.Equal(2, hit.BlockContributorCount);
        Assert.Contains("hero-widget-fold", hit.ContributingBlockKeys);
        Assert.Contains("footer-widget-fold", hit.ContributingBlockKeys);

        // Aggregate rank must be the MAX of the two block raw ranks, not the SUM.
        // Fetch the raw block ranks and compare.
        await using var ctx = _fixture.CreateContext();
        var blockRepo = new ContentBlockRepository(ctx);
        var rawBlocks = await blockRepo.SearchAsync("widgetfold", 20);
        var strongRank = rawBlocks.First(b => b.Key == "hero-widget-fold").Rank;
        var weakRank = rawBlocks.First(b => b.Key == "footer-widget-fold").Rank;
        var expectedMax = Math.Max(strongRank, weakRank);
        Assert.Equal(expectedMax, hit.AggregateRank);

        // Snippet is drawn from the higher-ranked contributor (the hero block).
        Assert.Contains("hero", hit.SnippetHtml, StringComparison.OrdinalIgnoreCase);
    }

    // ── Page + block on same URL ─────────────────────────────────────────────

    [Fact]
    [Trait("search-case", "duplicate-blocks")]
    public async Task SearchAsync_PageAndBlockOnSameUrl_ProduceOneCanonicalHit_WithPageAndBlockContributors()
    {
        await TruncateAsync();

        // Page's URL derives from PageContribution as "/" + page.Path — so a page with
        // Path = "check-your-pupil-data" produces URL "/check-your-pupil-data" which is
        // the same URL the block's static-route lookup resolves to.
        var page = BuildPage("check-your-pupil-data", "Check your pupil data", keywords: "mixedwidgetterm");
        await SeedPagesAsync((page, "the mixedwidgetterm appears in the page body"));

        var block = BuildBlock(
            "sidebar-mixed",
            keywords: "mixedwidgetterm",
            lastSeenPath: "/check-your-pupil-data",
            valuePlainText: "sidebar also mentions mixedwidgetterm");
        await SeedBlocksAsync(block);

        var result = await BuildSut().SearchAsync(new SiteSearchQuery(Query: "mixedwidgetterm"));

        var hit = Assert.Single(result.Hits);
        Assert.Equal("/check-your-pupil-data", hit.Url);
        Assert.Equal(1, hit.PageContributorCount);
        Assert.Equal(1, hit.BlockContributorCount);
        Assert.Equal("Check your pupil data", hit.Title);

        // Aggregate rank = MAX(pageRank, blockRank).
        await using var ctx = _fixture.CreateContext();
        var pageRepo = new PageNodeRepository(ctx);
        var blockRepo = new ContentBlockRepository(ctx);
        var rawPages = await pageRepo.SearchPagesAsync("mixedwidgetterm", scopePath: null, max: 20);
        var rawBlocks = await blockRepo.SearchAsync("mixedwidgetterm", 20);
        var pageRank = rawPages.First(p => p.PageId == page.Id).Rank;
        var blockRank = rawBlocks.First(b => b.Key == "sidebar-mixed").Rank;
        var expectedMax = Math.Max(pageRank, blockRank);
        Assert.Equal(expectedMax, hit.AggregateRank);
    }

    // ── Ordering by aggregate rank ──────────────────────────────────────────

    [Fact]
    [Trait("search-case", "duplicate-blocks")]
    public async Task SearchAsync_MultipleDistinctUrls_AreOrderedByAggregateRankDescending()
    {
        await TruncateAsync();

        // "stronger-page" matches on both Keywords (weight A) and Body, so its overall
        // ts_rank is higher than "weaker-page" which matches only via Body copy.
        var stronger = BuildPage("guidance/stronger-page", "Stronger page", keywords: "orderterm");
        var weaker = BuildPage("guidance/weaker-page", "Weaker page");
        await SeedPagesAsync(
            (stronger, "orderterm appears in the body of stronger-page"),
            (weaker, "just a single mention of orderterm at the tail end of a long body copy that goes on and on and on with unrelated words to dilute the rank"));

        var result = await BuildSut().SearchAsync(new SiteSearchQuery(Query: "orderterm"));

        // Two distinct URLs, both canonical.
        Assert.Equal(2, result.Hits.Count);
        Assert.Equal("/guidance/stronger-page", result.Hits[0].Url);
        Assert.Equal("/guidance/weaker-page", result.Hits[1].Url);
        Assert.True(result.Hits[0].AggregateRank > result.Hits[1].AggregateRank);
    }
}
