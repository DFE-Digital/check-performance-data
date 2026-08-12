using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.Search;
using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Controllers;

// AB#286387 R22: the /search endpoint must emit search_result_count so zero-result
// searches are visible in BigQuery. Mirrors the view's feedback-inset gate — the event
// fires only when a query was actually run (a non-empty CurrentQuery came back from the
// service), never on the empty-landing request and never on the DataStoreUnavailable
// (503) branch, which SearchControllerPaginationTests already pins as a non-emitting path.
public sealed class SearchControllerAnalyticsTests
{
    private readonly ISiteSearchService _searchService = Substitute.For<ISiteSearchService>();
    private readonly ISettingService _settings = Substitute.For<ISettingService>();
    private readonly IAnalyticsService _analytics = Substitute.For<IAnalyticsService>();

    private SearchController CreateSut(int totalCount = 0, SearchInvalidReason? invalidReason = null)
    {
        _settings.GetIntAsync(SettingKeys.CmsPageLength).Returns(20);
        _searchService
            .SearchAsync(Arg.Any<SiteSearchQuery>())
            .Returns(callInfo =>
            {
                var q = callInfo.Arg<SiteSearchQuery>();
                return Task.FromResult(new SiteSearchPagedResult
                {
                    CurrentQuery = q.Query ?? string.Empty,
                    ScopePath = q.ScopePath,
                    InvalidReason = invalidReason,
                    Hits = Array.Empty<CanonicalSearchHit>(),
                    TotalCount = totalCount,
                    Page = 0,
                    PageSize = q.PageSize,
                });
            });

        return new SearchController(_searchService, _settings, _analytics)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
            },
        };
    }

    [Fact]
    public async Task Index_QueryWithZeroHits_EmitsSearchResultCountEvent()
    {
        var sut = CreateSut(totalCount: 0);

        _ = await sut.Index("anything", scope: "guidance", includePages: null, includeContentBlocks: null);

        await _analytics.Received(1).TrackAsync(
            Arg.Is<SearchResultCountEvent>(e => e.ResultCount == 0 && e.Scope == "guidance"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Index_EmptyQuery_DoesNotEmitSearchResultCountEvent()
    {
        var sut = CreateSut(totalCount: 0, invalidReason: SearchInvalidReason.EmptyQuery);

        _ = await sut.Index(q: null, scope: null, includePages: null, includeContentBlocks: null);

        await _analytics.DidNotReceive().TrackAsync(Arg.Any<SearchResultCountEvent>(), Arg.Any<CancellationToken>());
    }
}
