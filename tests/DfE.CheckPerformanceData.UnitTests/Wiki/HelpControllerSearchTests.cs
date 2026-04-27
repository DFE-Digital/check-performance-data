using System.Reflection;
using DfE.CheckPerformanceData.Application.Wiki;
using DfE.CheckPerformanceData.Web.Controllers;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Wiki;

public sealed class HelpControllerSearchTests
{
    private readonly IWikiService _wikiService = Substitute.For<IWikiService>();
    private readonly HelpController _sut;

    public HelpControllerSearchTests()
    {
        _sut = new HelpController(_wikiService);
    }

    // --- Search action — validation ---

    [Fact]
    public async Task Search_EmptyQuery_RendersErrorSummary()
    {
        _wikiService.SearchAsync("", Arg.Any<int>(), Arg.Any<int>())
            .Returns(new WikiSearchResultsDto
            {
                Query = string.Empty,
                Page = 1,
                PageSize = 20,
                TotalCount = 0,
                Items = [],
                InvalidReason = SearchInvalidReason.EmptyQuery
            });

        var result = await _sut.Search(q: "", page: 1);

        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<SearchResultsViewModel>(view.Model);
        Assert.Equal(SearchInvalidReason.EmptyQuery, vm.InvalidReason);
        Assert.Contains("Enter a search term", vm.ErrorMessages);
    }

    [Fact]
    public async Task Search_SingleCharQuery_RendersMinLengthError()
    {
        _wikiService.SearchAsync("a", Arg.Any<int>(), Arg.Any<int>())
            .Returns(new WikiSearchResultsDto
            {
                Query = "a",
                Page = 1,
                PageSize = 20,
                TotalCount = 0,
                Items = [],
                InvalidReason = SearchInvalidReason.BelowMinimumLength
            });

        var result = await _sut.Search(q: "a", page: 1);

        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<SearchResultsViewModel>(view.Model);
        Assert.Equal(SearchInvalidReason.BelowMinimumLength, vm.InvalidReason);
        Assert.Contains("Enter at least 2 characters", vm.ErrorMessages);
    }

    [Fact]
    public async Task Search_RendersWithQueryPreFilled()
    {
        _wikiService.SearchAsync("publish", Arg.Any<int>(), Arg.Any<int>())
            .Returns(new WikiSearchResultsDto
            {
                Query = "publish",
                Page = 1,
                PageSize = 20,
                TotalCount = 1,
                Items = [ new() { Id = 1, Title = "P", Slug = "p", SlugPath = "p", SnippetHtml = "<p>x</p>" } ]
            });

        var result = await _sut.Search(q: "publish", page: 1);

        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<SearchResultsViewModel>(view.Model);
        Assert.Equal("publish", vm.CurrentQuery);
        Assert.Single(vm.Results);
        Assert.Null(vm.InvalidReason);
        Assert.Empty(vm.ErrorMessages);
    }

    [Fact]
    public async Task Search_ZeroResults_RendersEmptyState()
    {
        _wikiService.SearchAsync("nothing", Arg.Any<int>(), Arg.Any<int>())
            .Returns(new WikiSearchResultsDto
            {
                Query = "nothing",
                Page = 1,
                PageSize = 20,
                TotalCount = 0,
                Items = [],
                InvalidReason = null
            });

        var result = await _sut.Search(q: "nothing", page: 1);

        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<SearchResultsViewModel>(view.Model);
        Assert.Equal(0, vm.TotalCount);
        Assert.Empty(vm.Results);
        Assert.Null(vm.InvalidReason);
        Assert.Empty(vm.ErrorMessages);
    }

    // --- Pagination ---

    [Fact]
    public async Task Search_OutOfRangePage_Clamps()
    {
        // Service has already clamped — return page=2 even though page=99 was requested.
        _wikiService.SearchAsync("publish", 99, Arg.Any<int>())
            .Returns(new WikiSearchResultsDto
            {
                Query = "publish",
                Page = 2,
                PageSize = 20,
                TotalCount = 25,
                Items = [ new() { Id = 1, Title = "p", Slug = "p" } ]
            });

        var result = await _sut.Search(q: "publish", page: 99);

        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<SearchResultsViewModel>(view.Model);
        Assert.Equal(2, vm.CurrentPage);
        Assert.Equal(25, vm.TotalCount);
    }

    // --- Route wiring ---

    [Fact]
    public void Search_Action_Has_HttpGet_HelpSearch_AttributeRoute()
    {
        var method = typeof(HelpController).GetMethod(nameof(HelpController.Search))!;
        var attr = method.GetCustomAttribute<HttpGetAttribute>();
        Assert.NotNull(attr);
        Assert.Equal("help/search", attr!.Template);
    }
}
