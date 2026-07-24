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
        return new SiteSearchService(pageRepo, blockSearch, new FakeSearchTelemetry(), canonicaliser);
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

        // Seed a page whose Body / Title / Keywords do NOT match "canonicalise" so the
        // page corpus contributes nothing. Two blocks on the same URL match instead.
        var page = BuildPage("guidance/canon-test", "Canon Test");
        await SeedPagesAsync((page, "unrelated body copy"));

        // Different Keywords sets so the two blocks' ts_ranks differ — the MAX-scan is
        // testable rather than a coincidental tie.
        var strong = BuildBlock(
            "hero-canon",
            keywords: "canonicalise canonicalise canonicalise",
            lastSeenPath: "/guidance/canon-test",
            valuePlainText: "canonicalise here in the hero block");
        var weak = BuildBlock(
            "footer-canon",
            keywords: "other-term",
            lastSeenPath: "/guidance/canon-test",
            valuePlainText: "the footer briefly mentions canonicalise");
        await SeedBlocksAsync(strong, weak);

        var result = await BuildSut().SearchAsync(new SiteSearchQuery(Query: "canonicalise"));

        var hit = Assert.Single(result.Hits);
        Assert.Equal("/guidance/canon-test", hit.Url);
        Assert.Equal(0, hit.PageContributorCount);
        Assert.Equal(2, hit.BlockContributorCount);
        Assert.Contains("hero-canon", hit.ContributingBlockKeys);
        Assert.Contains("footer-canon", hit.ContributingBlockKeys);

        // Aggregate rank must be the MAX of the two block raw ranks, not the SUM.
        // Fetch the raw block ranks and compare.
        await using var ctx = _fixture.CreateContext();
        var blockRepo = new ContentBlockRepository(ctx);
        var rawBlocks = await blockRepo.SearchAsync("canonicalise", 20);
        var strongRank = rawBlocks.First(b => b.Key == "hero-canon").Rank;
        var weakRank = rawBlocks.First(b => b.Key == "footer-canon").Rank;
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

        // Page and block both match "mixed-widget-term"; both live at the same URL.
        var page = BuildPage("guidance/mixed-test", "Mixed Test", keywords: "mixed-widget-term");
        await SeedPagesAsync((page, "the mixed-widget-term appears in the body"));

        var block = BuildBlock(
            "sidebar-mixed",
            keywords: "mixed-widget-term",
            lastSeenPath: "/guidance/mixed-test",
            valuePlainText: "sidebar also mentions mixed-widget-term");
        await SeedBlocksAsync(block);

        var result = await BuildSut().SearchAsync(new SiteSearchQuery(Query: "mixed-widget-term"));

        var hit = Assert.Single(result.Hits);
        Assert.Equal("/guidance/mixed-test", hit.Url);
        Assert.Equal(1, hit.PageContributorCount);
        Assert.Equal(1, hit.BlockContributorCount);
        Assert.Equal("Mixed Test", hit.Title);

        // Aggregate rank = MAX(pageRank, blockRank).
        await using var ctx = _fixture.CreateContext();
        var pageRepo = new PageNodeRepository(ctx);
        var blockRepo = new ContentBlockRepository(ctx);
        var rawPages = await pageRepo.SearchPagesAsync("mixed-widget-term", scopePath: null, max: 20);
        var rawBlocks = await blockRepo.SearchAsync("mixed-widget-term", 20);
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
