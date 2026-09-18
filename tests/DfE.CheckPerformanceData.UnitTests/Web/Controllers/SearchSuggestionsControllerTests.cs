using DfE.CheckPerformanceData.Application.Search;
using DfE.CheckPerformanceData.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Controllers;

// Input contract for /search/suggestions, the typeahead behind the instant-search widget.
// It is called on every keystroke, so the guards that matter here are the cheap ones: a
// too-short term never reaches the database, a pasted essay is sliced before it does, and
// the caller can never ask for an unbounded number of rows.
//
// Method-level [Trait("search-case", ...)] is load-bearing — the coverage meta-test
// enumerates the trait across the search test assemblies and class-level traits are
// invisible to it.
public sealed class SearchSuggestionsControllerTests
{
    private readonly ISiteSearchService _searchService = Substitute.For<ISiteSearchService>();

    private SearchSuggestionsController CreateSut()
    {
        _searchService
            .SuggestAsync(Arg.Any<SiteSearchSuggestQuery>())
            .Returns(Task.FromResult<IReadOnlyList<SiteSearchSuggestion>>([]));

        return new SearchSuggestionsController(_searchService)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
    }

    [Fact]
    [Trait("search-case", "very-short")]
    public async Task ShortQuery_ReturnsEmpty_WithoutTouchingTheService()
    {
        var sut = CreateSut();

        var result = await sut.Suggestions("a", scope: null);

        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<SiteSearchSuggestion>>(
            Assert.IsType<JsonResult>(result).Value));
        await _searchService.DidNotReceive().SuggestAsync(Arg.Any<SiteSearchSuggestQuery>());
    }

    [Fact]
    [Trait("search-case", "empty-or-whitespace")]
    public async Task WhitespaceQuery_ReturnsEmpty_WithoutTouchingTheService()
    {
        var sut = CreateSut();

        await sut.Suggestions("   ", scope: null);

        await _searchService.DidNotReceive().SuggestAsync(Arg.Any<SiteSearchSuggestQuery>());
    }

    [Fact]
    [Trait("search-case", "long-query")]
    public async Task LongQuery_IsSlicedToOneHundredCharacters()
    {
        var sut = CreateSut();

        await sut.Suggestions(new string('a', 500), scope: null);

        await _searchService.Received(1).SuggestAsync(
            Arg.Is<SiteSearchSuggestQuery>(q => q.Query != null && q.Query.Length == 100));
    }

    [Fact]
    [Trait("search-case", "scope-filter")]
    public async Task Scope_IsPassedThroughToTheService()
    {
        var sut = CreateSut();

        await sut.Suggestions("merge", scope: "guidance");

        await _searchService.Received(1).SuggestAsync(
            Arg.Is<SiteSearchSuggestQuery>(q => q.ScopePath == "guidance"));
    }

    [Fact]
    public async Task TheNumberOfSuggestionsAskedFor_IsCapped()
    {
        var sut = CreateSut();

        await sut.Suggestions("merge", scope: null);

        await _searchService.Received(1).SuggestAsync(
            Arg.Is<SiteSearchSuggestQuery>(q => q.Limit > 0 && q.Limit <= 10));
    }

    [Fact]
    public async Task Suggestions_AreReturnedAsLabelAndUrl()
    {
        _searchService
            .SuggestAsync(Arg.Any<SiteSearchSuggestQuery>())
            .Returns(Task.FromResult<IReadOnlyList<SiteSearchSuggestion>>(
                [new SiteSearchSuggestion("Merge pupil records", "/help/merge")]));

        var sut = new SearchSuggestionsController(_searchService)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        var json = Assert.IsType<JsonResult>(await sut.Suggestions("merge", scope: null));
        var payload = Assert.IsAssignableFrom<IReadOnlyList<SiteSearchSuggestion>>(json.Value);

        Assert.Equal("Merge pupil records", payload[0].Label);
        Assert.Equal("/help/merge", payload[0].Url);
    }
}
