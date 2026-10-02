using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.Search;
using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.Web.Controllers;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Controllers;

// /search?pages=t1,t2 names pages by token. The controller hands the tokens to the search
// service, which looks them up; what it shows and records is the readable paths it got back,
// while the links it builds keep the short form the request arrived with.
public sealed class SearchControllerPageTokensTests
{
    private const string Resolved = "guidance/post-16,help";

    private readonly ISiteSearchService _searchService = Substitute.For<ISiteSearchService>();
    private readonly ISettingService _settings = Substitute.For<ISettingService>();
    private readonly IAnalyticsService _analytics = Substitute.For<IAnalyticsService>();

    private SearchController CreateSut()
    {
        _settings.GetIntAsync(SettingKeys.CmsPageLength).Returns(20);
        _searchService.SearchAsync(Arg.Any<SiteSearchQuery>()).Returns(callInfo =>
        {
            var q = callInfo.Arg<SiteSearchQuery>();
            return Task.FromResult(new SiteSearchPagedResult
            {
                CurrentQuery = q.Query ?? string.Empty,
                ScopePath = Resolved,
                InvalidReason = null,
                Hits = Array.Empty<CanonicalSearchHit>(),
                TotalCount = 0,
                Page = 0,
                PageSize = q.PageSize,
            });
        });

        return new SearchController(_searchService, _settings, _analytics)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
    }

    [Fact]
    [Trait("search-case", "scope-filter")]
    public async Task Index_PassesPageTokensAndScopeToTheService()
    {
        await CreateSut().Index("evidence", scope: "/help/", includePages: null, includeContentBlocks: null,
            pages: " fiSE59QT,QxpbTxwJ ");

        await _searchService.Received(1).SearchAsync(Arg.Is<SiteSearchQuery>(q =>
            q.ScopePath == "/help/" && q.PageTokens == " fiSE59QT,QxpbTxwJ "));
    }

    [Fact]
    [Trait("search-case", "scope-filter")]
    public async Task Index_ShowsTheResolvedPaths_AndKeepsTheRequestFormForLinks()
    {
        var result = await CreateSut().Index("evidence", scope: "/help/", includePages: null,
            includeContentBlocks: null, pages: " fiSE59QT,QxpbTxwJ ");

        var model = Assert.IsType<SiteSearchViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal(Resolved, model.Scope);
        Assert.Equal("help", model.QueryScope);
        Assert.Equal("fiSE59QT,QxpbTxwJ", model.QueryPages);
    }

    // The analytics event records readable paths, never the tokens, so a report reads the same
    // whichever way the search named its pages.
    [Fact]
    public async Task Index_RecordsTheResolvedPathsAsTheScope()
    {
        await CreateSut().Index("evidence", scope: null, includePages: null, includeContentBlocks: null,
            pages: "fiSE59QT,QxpbTxwJ");

        await _analytics.Received(1).TrackAsync(
            Arg.Is<SearchResultCountEvent>(e => e.Scope == Resolved),
            Arg.Any<CancellationToken>());
    }
}
