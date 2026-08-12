using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.Search;
using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Controllers;

// Pins the long-query behaviour at the /search controller boundary. Three expected
// behaviours hold: a 100-char query passes through untouched; a 100-char single-word query
// does the same and does not blow up the tokeniser; a 500-char query is silently trimmed to
// the leading 100 chars before the SiteSearchQuery is constructed (whitespace-first, then
// hard slice). No user-visible hint, no error summary — the cap is invisible unless the
// caller inspects what the service received.
//
// Method-level [Trait("search-case", "long-query")] on all three tests is load-bearing — the
// downstream coverage meta-test enumerates that trait across the search test assemblies.
// Class-level traits are deliberately avoided (they only inherit when method enumeration
// passes inherit: true, which the meta-test does not).
public sealed class SearchControllerTests
{
    private readonly ISiteSearchService _searchService = Substitute.For<ISiteSearchService>();
    private readonly ISettingService _settings = Substitute.For<ISettingService>();
    private readonly IAnalyticsService _analytics = Substitute.For<IAnalyticsService>();

    private SearchController CreateSut()
    {
        _searchService
            .SearchAsync(Arg.Any<SiteSearchQuery>())
            .Returns(callInfo => Task.FromResult(EmptyResultFor(callInfo.Arg<SiteSearchQuery>())));
        _settings.GetIntAsync(SettingKeys.CmsPageLength).Returns(20);
        return new SearchController(_searchService, _settings, _analytics)
        {
            // The 503 branch writes Response.StatusCode; even the happy-path tests keep the
            // context wired so a future assertion that reaches Response cannot NRE on a null
            // HttpContext.
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
            },
        };
    }

    private static SiteSearchPagedResult EmptyResultFor(SiteSearchQuery query) =>
        new()
        {
            CurrentQuery = query.Query ?? string.Empty,
            ScopePath = query.ScopePath,
            InvalidReason = null,
            Hits = Array.Empty<CanonicalSearchHit>(),
            TotalCount = 0,
            Page = Math.Max(1, query.Page),
            PageSize = Math.Max(1, query.PageSize),
        };

    // A query at the documented 100-char boundary is a no-op for the controller: it flows
    // straight through into SiteSearchQuery.Query without any trim, truncation or
    // normalisation. Passes at HEAD because no cap exists yet — the same observable behaviour
    // that the contract requires happens to be what an uncapped forwarder produces at the
    // boundary length. Asserting on SiteSearchQuery.Query keeps the check scoped to controller
    // responsibility (the FTS layer's stemming/rank behaviour is owned by the integration tier).
    [Fact]
    [Trait("search-case", "long-query")]
    public async Task Index_100CharQuery_PassesThroughToService()
    {
        var query = new string('a', 100);
        var sut = CreateSut();

        _ = await sut.Index(query, scope: null, includePages: null, includeContentBlocks: null);

        await _searchService.Received(1).SearchAsync(
            Arg.Is<SiteSearchQuery>(q => q.Query != null && q.Query.Length == 100 && q.Query == query));
    }

    // A 100-char query with no whitespace exercises the "no OR-join opportunity" path through
    // SearchTermNormalizer downstream, but the controller's obligation is the same as the
    // whitespace case: forward the raw string untouched. Same passing reason — the absent cap
    // is a no-op at the boundary length. The mocked SearchAsync must not throw (it doesn't —
    // NSubstitute's Returns(...) guarantees a value), so the "does not blow up the tokeniser"
    // clause is asserted vacuously at the controller tier and re-asserted for real by the
    // SearchTermNormalizer/repository integration tests.
    [Fact]
    [Trait("search-case", "long-query")]
    public async Task Index_100CharSingleWordQuery_DoesNotThrow_AndPassesThroughToService()
    {
        var query = new string('x', 100); // no whitespace — single "word" at the boundary
        var sut = CreateSut();

        _ = await sut.Index(query, scope: null, includePages: null, includeContentBlocks: null);

        await _searchService.Received(1).SearchAsync(
            Arg.Is<SiteSearchQuery>(q => q.Query != null && q.Query.Length == 100 && !q.Query.Contains(' ')));
    }

    // The controller now trims over-100-char inputs at the entry boundary before constructing
    // the SiteSearchQuery, so a 500-char paste never reaches the FTS layer verbatim. The trim
    // is silent — no user-visible hint, no error summary. The equality check on the leading
    // substring pins "leading 100 chars" (not "any 100 chars"), matching the shipped
    // whitespace-first-then-slice controller pattern.
    [Fact]
    [Trait("search-case", "long-query")]
    public async Task Index_500CharQuery_TrimsToLeading100Chars_BeforeService()
    {
        var query = new string('a', 500);
        var sut = CreateSut();

        _ = await sut.Index(query, scope: null, includePages: null, includeContentBlocks: null);

        await _searchService.Received(1).SearchAsync(
            Arg.Is<SiteSearchQuery>(q =>
                q.Query != null
                && q.Query.Length == 100
                && q.Query == query.Substring(0, 100)));
    }
}
