using System.Net;
using DfE.CheckPerformanceData.Application.Search;
using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.Web.Controllers;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Controllers;

// Unit-tier verification of SearchController.Index's paging + resilience surface.
//
// The controller resolves pageSize in this order per request:
//   1. Explicit ?pageSize query-string wins.
//   2. Otherwise ISettingService.GetIntAsync(SettingKeys.CmsPageLength) supplies the shared
//      admin knob.
//   3. Anything less than 1 falls back to 20 (defence-in-depth).
//   4. Whatever survives the fallbacks is Math.Clamp'd into [10, 50].
//
// The ?page query-string is clamped to at least 1 up front; the upper clamp cannot land
// until the service reports TotalPages, so an over-clamp forces a second SearchAsync call
// at the last valid page. When the service comes back with
// SearchInvalidReason.DataStoreUnavailable, the controller writes HTTP 503, renders the
// "Unavailable" view and preserves the user's typed query on the retry link. The DB-down
// branch MUST NOT re-call the service — Plan 02 wired the guard so a bad-page-plus-outage
// combination cannot amplify a downstream failure.
//
// The DB-unavailable slug is deliberately excluded from SearchCaseCoverageTests's expected
// slug set; a resilience-focused integration suite owns that behaviour end-to-end.
// Method-level [Trait("search-case", <slug>)] on every fact/theory is load-bearing — the
// downstream coverage meta-test enumerates traits across the search test assemblies.
public sealed class SearchControllerPaginationTests
{
    private readonly ISiteSearchService _searchService = Substitute.For<ISiteSearchService>();
    private readonly ISettingService _settings = Substitute.For<ISettingService>();

    // Compose a controller with a mocked service that echoes the requested paging window back
    // to the caller. The optional invalidReason lets a single helper drive both the happy path
    // and the 503 branch; hits + totalCount let the over-clamp test seed a corpus large enough
    // for the controller's re-issue logic to bite.
    private SearchController CreateSut(
        int cmsPageLength = 20,
        SearchInvalidReason? invalidReason = null,
        int totalCount = 0,
        IReadOnlyList<CanonicalSearchHit>? hits = null)
    {
        _settings.GetIntAsync(SettingKeys.CmsPageLength).Returns(cmsPageLength);
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
                    Hits = hits ?? Array.Empty<CanonicalSearchHit>(),
                    TotalCount = totalCount,
                    // Internal state is zero-indexed to match the shared pagination view model;
                    // the controller re-serialises to the one-indexed URL boundary on render.
                    Page = q.Page - 1,
                    PageSize = q.PageSize,
                });
            });

        var controller = new SearchController(_searchService, _settings)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
            },
        };
        return controller;
    }

    // --- pageSize resolution + clamp ---

    // Absent ?pageSize, the setting supplies the value; the shipped default of 20 is what
    // /search hands to the service when no admin override is stored.
    [Fact]
    [Trait("search-case", "pagination")]
    public async Task Index_PageSizeUnset_ResolvesFromSettingService_Default20()
    {
        var sut = CreateSut(cmsPageLength: 20);

        _ = await sut.Index("demo", scope: null, includePages: null, includeContentBlocks: null, page: null, pageSize: null);

        await _searchService.Received(1).SearchAsync(Arg.Is<SiteSearchQuery>(q => q.PageSize == 20));
    }

    // A non-default admin setting inside the [10, 50] window rides through untouched — no
    // implicit floor above 20, no round-tripping to the code default.
    [Fact]
    [Trait("search-case", "pagination")]
    public async Task Index_PageSizeUnset_ResolvesFromCustomCmsPageLength30()
    {
        var sut = CreateSut(cmsPageLength: 30);

        _ = await sut.Index("demo", scope: null, includePages: null, includeContentBlocks: null, page: null, pageSize: null);

        await _searchService.Received(1).SearchAsync(Arg.Is<SiteSearchQuery>(q => q.PageSize == 30));
    }

    // Explicit ?pageSize beats the admin setting per request; a value already inside the
    // clamp window flows through unchanged.
    [Fact]
    [Trait("search-case", "pagination")]
    public async Task Index_PageSizeQueryStringOverridesSetting_Clamped()
    {
        var sut = CreateSut(cmsPageLength: 20);

        _ = await sut.Index("demo", scope: null, includePages: null, includeContentBlocks: null, page: null, pageSize: 25);

        await _searchService.Received(1).SearchAsync(Arg.Is<SiteSearchQuery>(q => q.PageSize == 25));
    }

    // Below-floor ?pageSize is silently promoted to 10; nothing is rejected.
    [Fact]
    [Trait("search-case", "pagination")]
    public async Task Index_PageSizeBelow10_ClampsTo10()
    {
        var sut = CreateSut(cmsPageLength: 20);

        _ = await sut.Index("demo", scope: null, includePages: null, includeContentBlocks: null, page: null, pageSize: 5);

        await _searchService.Received(1).SearchAsync(Arg.Is<SiteSearchQuery>(q => q.PageSize == 10));
    }

    // Above-ceiling ?pageSize is silently capped at 50 — a pathologically large
    // request never inflates the per-corpus fetch.
    [Fact]
    [Trait("search-case", "pagination")]
    public async Task Index_PageSizeAbove50_ClampsTo50()
    {
        var sut = CreateSut(cmsPageLength: 20);

        _ = await sut.Index("demo", scope: null, includePages: null, includeContentBlocks: null, page: null, pageSize: 100);

        await _searchService.Received(1).SearchAsync(Arg.Is<SiteSearchQuery>(q => q.PageSize == 50));
    }

    // An admin knob above the shared ceiling gets clamped at read time — the setting is not
    // a super-power, just a default. Pins the clamp-after-fallback ordering.
    [Fact]
    [Trait("search-case", "pagination")]
    public async Task Index_PageSizeSettingValue60_ClampsTo50()
    {
        var sut = CreateSut(cmsPageLength: 60);

        _ = await sut.Index("demo", scope: null, includePages: null, includeContentBlocks: null, page: null, pageSize: null);

        await _searchService.Received(1).SearchAsync(Arg.Is<SiteSearchQuery>(q => q.PageSize == 50));
    }

    // --- page clamp ---

    // Negative / zero ?page is silently corrected to 1; no error surfaced to the user.
    [Fact]
    [Trait("search-case", "pagination")]
    public async Task Index_PageBelow1_ClampsTo1()
    {
        var sut = CreateSut(cmsPageLength: 20);

        _ = await sut.Index("demo", scope: null, includePages: null, includeContentBlocks: null, page: -5, pageSize: null);

        await _searchService.Received(1).SearchAsync(Arg.Is<SiteSearchQuery>(q => q.Page == 1));
    }

    // Over-clamp costs one extra SearchAsync call in the pathological case: the first call
    // proves the corpus size, the second re-issues at the last valid page. Received(2) pins
    // the count so a regression that loops the re-issue cannot mask itself.
    [Fact]
    [Trait("search-case", "pagination")]
    public async Task Index_PageAboveTotalPages_ClampsToLastValidPage()
    {
        var singleHit = new CanonicalSearchHit(
            Url: "/demo",
            Title: "Demo",
            SnippetHtml: "demo",
            AggregateRank: 1.0f,
            PageContributorCount: 1,
            BlockContributorCount: 0,
            ContributingBlockKeys: Array.Empty<string>());
        var sut = CreateSut(cmsPageLength: 20, totalCount: 45, hits: new[] { singleHit });

        var result = await sut.Index("demo", scope: null, includePages: null, includeContentBlocks: null, page: 99, pageSize: null);

        await _searchService.Received(2).SearchAsync(Arg.Any<SiteSearchQuery>());
        // TotalPages = ceil(45 / 20) = 3 — the second call must land there exactly.
        await _searchService.Received(1).SearchAsync(Arg.Is<SiteSearchQuery>(q => q.Page == 99));
        await _searchService.Received(1).SearchAsync(Arg.Is<SiteSearchQuery>(q => q.Page == 3));

        var view = Assert.IsType<ViewResult>(result);
        Assert.NotEqual("Unavailable", view.ViewName);
        var model = Assert.IsType<SiteSearchViewModel>(view.Model);
        Assert.Equal(3, model.Page);
    }

    // --- 503 branch ---

    // The DB-outage branch renders the "Unavailable" view at HTTP 503 and keeps the user's
    // typed query on the model so the retry link round-trips it.
    [Fact]
    [Trait("search-case", "db-unavailable")]
    public async Task Index_ServiceReturnsDataStoreUnavailable_RendersUnavailableView_With503()
    {
        var sut = CreateSut(cmsPageLength: 20, invalidReason: SearchInvalidReason.DataStoreUnavailable);

        var result = await sut.Index("demo", scope: null, includePages: null, includeContentBlocks: null, page: 1, pageSize: 20);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Unavailable", view.ViewName);
        Assert.Equal((int)HttpStatusCode.ServiceUnavailable, sut.Response.StatusCode);
        var model = Assert.IsType<SiteSearchViewModel>(view.Model);
        Assert.Equal("demo", model.Query);
    }

    // The 503 path exits before the over-clamp re-issue so a bad-page-plus-outage combo
    // cannot amplify a downstream failure into a second DB call.
    [Fact]
    [Trait("search-case", "db-unavailable")]
    public async Task Index_ServiceReturnsDataStoreUnavailable_DoesNotCallServiceASecondTime()
    {
        var sut = CreateSut(cmsPageLength: 20, invalidReason: SearchInvalidReason.DataStoreUnavailable);

        _ = await sut.Index("demo", scope: null, includePages: null, includeContentBlocks: null, page: 1, pageSize: 20);

        await _searchService.Received(1).SearchAsync(Arg.Any<SiteSearchQuery>());
    }
}
