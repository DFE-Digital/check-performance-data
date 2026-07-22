using DfE.CheckPerformanceData.Application.ContentBlocks;
using DfE.CheckPerformanceData.Application.PageTree;
using DfE.CheckPerformanceData.Application.Search;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Search;

// Guards the /search + widget-scope contract: query normalisation, empty-query short-circuit,
// scope-path passthrough to the page repository, and the client-side URL-prefix filter that
// keeps content-block hits inside the requested subtree.
public class SiteSearchServiceTests
{
    private readonly IPageNodeRepository _pageRepo = Substitute.For<IPageNodeRepository>();
    private readonly IContentBlockSearchService _blockSearch = Substitute.For<IContentBlockSearchService>();
    private readonly SiteSearchService _sut;

    public SiteSearchServiceTests()
    {
        _sut = new SiteSearchService(_pageRepo, _blockSearch);
        _pageRepo.SearchPagesAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>())
            .Returns([]);
        _blockSearch.SearchAsync(Arg.Any<string?>(), Arg.Any<int>())
            .Returns([]);
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
        _blockSearch.SearchAsync("ks4", Arg.Any<int>()).Returns(new List<ContentBlockSearchResultDto>
        {
            new() { Key = "in", Url = "/guidance/ks4-june-2026-overview", PageTitle = "Overview", SnippetHtml = "" },
            new() { Key = "root", Url = "/guidance", PageTitle = "Guidance", SnippetHtml = "" },
            new() { Key = "out", Url = "/help/some-other-page", PageTitle = "Other", SnippetHtml = "" }
        });

        var result = await _sut.SearchAsync(new SiteSearchQuery(
            Query: "ks4",
            ScopePath: "guidance",
            IncludePages: false,
            IncludeContentBlocks: true));

        Assert.Collection(result.ContentBlockHits.OrderBy(h => h.Key),
            h => Assert.Equal("in", h.Key),
            h => Assert.Equal("root", h.Key));
    }

    // Content-block hits must not accidentally match on shared prefixes: /guidance-foo/x is not
    // inside /guidance. Only /guidance or /guidance/ prefixes count.
    [Fact]
    [Trait("search-case", "scope-filter")]
    public async Task SearchAsync_WithScopePath_DoesNotAcceptPrefixCollisionsFromSiblings()
    {
        _blockSearch.SearchAsync("x", Arg.Any<int>()).Returns(new List<ContentBlockSearchResultDto>
        {
            new() { Key = "sibling", Url = "/guidance-archive/legacy", PageTitle = "Archive", SnippetHtml = "" }
        });

        var result = await _sut.SearchAsync(new SiteSearchQuery(
            Query: "x",
            ScopePath: "guidance",
            IncludePages: false,
            IncludeContentBlocks: true));

        Assert.Empty(result.ContentBlockHits);
    }
}
