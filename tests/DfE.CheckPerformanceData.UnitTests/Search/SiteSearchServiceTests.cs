using DfE.CheckPerformanceData.Application.ContentBlocks;
using DfE.CheckPerformanceData.Application.PageTree;
using DfE.CheckPerformanceData.Application.Search;
using DfE.CheckPerformanceData.Application.UnitTests.Search.TestDoubles;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Search;

// Guards the /search + widget-scope contract: query normalisation, empty-query short-circuit,
// scope-path passthrough to the page repository, and the client-side URL-prefix filter that
// keeps content-block hits inside the requested subtree. Also guards the telemetry emission
// contract — one SearchTelemetryEvent per top-level SearchAsync call, per-corpus latency,
// per-field rank threading, and the invalid-query non-emission.
public class SiteSearchServiceTests
{
    private readonly IPageNodeRepository _pageRepo = Substitute.For<IPageNodeRepository>();
    private readonly IContentBlockSearchService _blockSearch = Substitute.For<IContentBlockSearchService>();
    private readonly FakeSearchTelemetry _telemetry = new();
    private readonly SiteSearchService _sut;

    public SiteSearchServiceTests()
    {
        _sut = new SiteSearchService(_pageRepo, _blockSearch, _telemetry, new SearchResultCanonicaliser(), Microsoft.Extensions.Logging.Abstractions.NullLogger<SiteSearchService>.Instance);
        _pageRepo.SearchPagesAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>())
            .Returns([]);
        _blockSearch.SearchAsync(Arg.Any<string?>(), Arg.Any<int>())
            .Returns(new ContentBlockSearchOutcome([], []));
    }

    // Widget scope prop arrives with slashes ("/guidance/") — service must normalise
    // to the bare segment before handing off to the repository.
    [Fact]
    public async Task SearchAsync_TrimsSurroundingSlashesFromScopePath_BeforeCallingRepository()
    {
        await _sut.SearchAsync(new SiteSearchQuery(
            Query: "ks4",
            ScopePath: "/guidance/",
            IncludePages: true,
            IncludeContentBlocks: false));

        await _pageRepo.Received(1).SearchPagesAsync("ks4", "guidance", Arg.Any<int>());
    }

    // Whitespace-only scope is indistinguishable from "no scope"; the repo call must not
    // include a phantom filter.
    [Fact]
    public async Task SearchAsync_WhenScopePathIsWhitespace_PassesNullScopeToRepository()
    {
        await _sut.SearchAsync(new SiteSearchQuery(
            Query: "ks4",
            ScopePath: "   ",
            IncludePages: true,
            IncludeContentBlocks: false));

        await _pageRepo.Received(1).SearchPagesAsync("ks4", null, Arg.Any<int>());
    }

    // Below-minimum queries return an InvalidReason without touching either search backend.
    [Fact]
    [Trait("search-case", "very-short")]
    public async Task SearchAsync_BelowMinimumLengthQuery_ShortCircuits_WithoutHittingSearchBackends()
    {
        var result = await _sut.SearchAsync(new SiteSearchQuery(
            Query: "a",
            IncludePages: true,
            IncludeContentBlocks: true));

        Assert.Equal(SearchInvalidReason.BelowMinimumLength, result.InvalidReason);
        await _pageRepo.DidNotReceive().SearchPagesAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>());
        await _blockSearch.DidNotReceive().SearchAsync(Arg.Any<string?>(), Arg.Any<int>());
    }

    // Scoping content-block hits: only URLs equal to /{scope} or starting /{scope}/ survive.
    // Guards the KS4-split-page use case where a scoped Search widget must not surface blocks
    // that live on unrelated pages.
    [Fact]
    [Trait("search-case", "scope-filter")]
    public async Task SearchAsync_WithScopePath_KeepsOnlyContentBlocksUnderThatSubtree()
    {
        _blockSearch.SearchAsync("ks4", Arg.Any<int>()).Returns(new ContentBlockSearchOutcome(
            new List<ContentBlockSearchResultDto>
            {
                new() { Key = "in", Url = "/guidance/ks4-june-2026-overview", PageTitle = "Overview", SnippetHtml = "" },
                new() { Key = "root", Url = "/guidance", PageTitle = "Guidance", SnippetHtml = "" },
                new() { Key = "out", Url = "/help/some-other-page", PageTitle = "Other", SnippetHtml = "" }
            },
            []));

        var result = await _sut.SearchAsync(new SiteSearchQuery(
            Query: "ks4",
            ScopePath: "guidance",
            IncludePages: false,
            IncludeContentBlocks: true));

        // Scope filter drops "out"; the two remaining blocks each canonicalise to their
        // own URL row (distinct URLs → distinct canonical hits). Contributing keys ride
        // on the row so scope enforcement is still asserted per source block.
        var byUrl = result.Hits.OrderBy(h => h.Url).ToList();
        Assert.Equal(2, byUrl.Count);
        Assert.Equal("/guidance", byUrl[0].Url);
        Assert.Contains("root", byUrl[0].ContributingBlockKeys);
        Assert.Equal("/guidance/ks4-june-2026-overview", byUrl[1].Url);
        Assert.Contains("in", byUrl[1].ContributingBlockKeys);
    }

    // Content-block hits must not accidentally match on shared prefixes: /guidance-foo/x is not
    // inside /guidance. Only /guidance or /guidance/ prefixes count.
    [Fact]
    [Trait("search-case", "scope-filter")]
    public async Task SearchAsync_WithScopePath_DoesNotAcceptPrefixCollisionsFromSiblings()
    {
        _blockSearch.SearchAsync("x", Arg.Any<int>()).Returns(new ContentBlockSearchOutcome(
            new List<ContentBlockSearchResultDto>
            {
                new() { Key = "sibling", Url = "/guidance-archive/legacy", PageTitle = "Archive", SnippetHtml = "" }
            },
            []));

        var result = await _sut.SearchAsync(new SiteSearchQuery(
            Query: "x",
            ScopePath: "guidance",
            IncludePages: false,
            IncludeContentBlocks: true));

        Assert.Empty(result.Hits);
    }

    // ── Telemetry emission contract ────────────────────────────────────────────

    [Fact]
    [Trait("search-case", "rank-breakdown-telemetry")]
    public async Task SearchAsync_EmitsExactlyOneEventPerRequest()
    {
        _pageRepo.SearchPagesAsync("widget", Arg.Any<string?>(), Arg.Any<int>()).Returns(
        [
            Page(Guid.NewGuid(), "a", "A"),
        ]);
        _blockSearch.SearchAsync("widget", Arg.Any<int>()).Returns(new ContentBlockSearchOutcome(
            [Block("k1", "/a", "A")], []));

        await _sut.SearchAsync(new SiteSearchQuery(Query: "widget"));

        Assert.Single(_telemetry.Events);
    }

    [Fact]
    public async Task SearchAsync_EmittedEventCarriesQueryRawAndNormalisedAndScope()
    {
        await _sut.SearchAsync(new SiteSearchQuery(
            Query: "hello world",
            ScopePath: "/guidance/"));

        var evt = Assert.Single(_telemetry.Events);
        Assert.Equal("hello world", evt.QueryRaw);
        // Normaliser OR-joins whitespace-separated tokens on the trimmed term.
        Assert.Equal("hello OR world", evt.QueryNormalised);
        Assert.Equal("guidance", evt.Scope);
    }

    [Fact]
    [Trait("search-case", "rank-breakdown-telemetry")]
    public async Task SearchAsync_WhenIncludePagesFalse_LatencyMsPagesIsNull()
    {
        await _sut.SearchAsync(new SiteSearchQuery(
            Query: "widget",
            IncludePages: false,
            IncludeContentBlocks: true));

        var evt = Assert.Single(_telemetry.Events);
        Assert.Null(evt.LatencyMsPages);
        Assert.NotNull(evt.LatencyMsBlocks);
    }

    [Fact]
    [Trait("search-case", "rank-breakdown-telemetry")]
    public async Task SearchAsync_WhenIncludeContentBlocksFalse_LatencyMsBlocksIsNull()
    {
        await _sut.SearchAsync(new SiteSearchQuery(
            Query: "widget",
            IncludePages: true,
            IncludeContentBlocks: false));

        var evt = Assert.Single(_telemetry.Events);
        Assert.Null(evt.LatencyMsBlocks);
        Assert.NotNull(evt.LatencyMsPages);
    }

    [Fact]
    [Trait("search-case", "rank-breakdown-telemetry")]
    public async Task SearchAsync_EmittedHitsCarryPerFieldRankBreakdown()
    {
        var pageId = Guid.NewGuid();
        _pageRepo.SearchPagesAsync("widget", Arg.Any<string?>(), Arg.Any<int>()).Returns(
        [
            new PageSearchHitRaw
            {
                PageId = pageId,
                Path = "guidance/one",
                Title = "One",
                Subtitle = null,
                BodyPlainText = "widget in body",
                Rank = 0.5f,
                RankKeywords = 0.1f,
                RankTitle = 0.2f,
                RankSubtitle = 0.05f,
                RankBody = 0.15f,
            },
        ]);
        _blockSearch.SearchAsync("widget", Arg.Any<int>()).Returns(new ContentBlockSearchOutcome(
            [
                new ContentBlockSearchResultDto
                {
                    Key = "b1",
                    Url = "/guidance/one#a",
                    PageTitle = "One",
                    SnippetHtml = "",
                    Rank = 0.3f,
                    RankKeywords = 0.11f,
                    RankValue = 0.19f,
                }
            ],
            []));

        await _sut.SearchAsync(new SiteSearchQuery(Query: "widget"));

        // Per-canonical emission: page URL and block URL differ, so two evt.Hits — one
        // page-only canonical (PageContributed=true, BlockContributorCount=0) with the
        // page's per-field ranks, and one block-only canonical (PageContributed=false,
        // BlockContributorCount=1) with the block's per-field ranks. The block's key is
        // preserved on ContributingBlockKeys.
        var evt = Assert.Single(_telemetry.Events);
        Assert.Equal(2, evt.Hits.Count);

        var pageHit = Assert.Single(evt.Hits, h => h.PageContributed && h.BlockContributorCount == 0);
        Assert.Equal("/guidance/one", pageHit.Url);
        Assert.Empty(pageHit.ContributingBlockKeys);
        Assert.Equal(0.1f, pageHit.RankKeywords);
        Assert.Equal(0.2f, pageHit.RankTitle);
        Assert.Equal(0.05f, pageHit.RankSubtitle);
        Assert.Equal(0.15f, pageHit.RankBody);
        Assert.Null(pageHit.RankValue);

        var blockHit = Assert.Single(evt.Hits, h => !h.PageContributed && h.BlockContributorCount > 0);
        Assert.Equal("/guidance/one#a", blockHit.Url);
        Assert.Contains("b1", blockHit.ContributingBlockKeys);
        Assert.Equal(0.11f, blockHit.RankKeywords);
        Assert.Equal(0.19f, blockHit.RankValue);
        Assert.Null(blockHit.RankTitle);
        Assert.Null(blockHit.RankSubtitle);
        Assert.Null(blockHit.RankBody);
    }

    [Fact]
    public async Task SearchAsync_InvalidQuery_DoesNotEmitEvent()
    {
        await _sut.SearchAsync(new SiteSearchQuery(Query: ""));

        Assert.Empty(_telemetry.Events);
    }

    private static PageSearchHitRaw Page(Guid id, string path, string title) => new()
    {
        PageId = id,
        Path = path,
        Title = title,
        Subtitle = null,
        BodyPlainText = string.Empty,
        Rank = 1f,
    };

    private static ContentBlockSearchResultDto Block(string key, string url, string pageTitle) => new()
    {
        Key = key,
        Url = url,
        PageTitle = pageTitle,
        SnippetHtml = string.Empty,
        Rank = 1f,
    };
}
