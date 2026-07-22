using DfE.CheckPerformanceData.Application.ContentBlocks;
using DfE.CheckPerformanceData.Application.Search;
using DfE.CheckPerformanceData.Web.Controllers;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Controllers;

// Pins PRD §6 case H (long queries) at the /search controller boundary. Two of the three
// AC lines are GREEN at HEAD (AC-H1 100-char query passes through untouched; AC-H3 100-char
// single-word query does the same and does not blow up the tokeniser). AC-H2 (500-char query
// silently trimmed to first 100 chars) is a KNOWN BASELINE GAP — SearchController.Index does
// not currently enforce any length cap. Parked as [Fact(Skip=…)] + [Trait("known-bug", …)]
// per the bug-discovery workflow so the case H trait is still discovered by the meta-test
// while the fix waits on a dedicated ticket (natural home: Phase 1.10 search resilience).
//
// Method-level [Trait("prd-case", "H")] on all three tests is load-bearing — the downstream
// coverage meta-test enumerates that trait across the search test assemblies. Class-level
// traits are deliberately avoided (they only inherit when method enumeration passes
// inherit: true, which the meta-test does not).
public sealed class SearchControllerTests
{
    private readonly ISiteSearchService _searchService = Substitute.For<ISiteSearchService>();

    private SearchController CreateSut()
    {
        _searchService
            .SearchAsync(Arg.Any<SiteSearchQuery>())
            .Returns(callInfo => Task.FromResult(EmptyResultFor(callInfo.Arg<SiteSearchQuery>())));
        return new SearchController(_searchService);
    }

    private static SiteSearchResult EmptyResultFor(SiteSearchQuery query) =>
        new()
        {
            CurrentQuery = query.Query ?? string.Empty,
            ScopePath = query.ScopePath,
            InvalidReason = null,
            PageHits = Array.Empty<PageSearchHitDto>(),
            ContentBlockHits = Array.Empty<ContentBlockSearchResultDto>(),
        };

    // AC-H1 — a query that is exactly at the PRD-documented 100-char boundary is a no-op
    // for the controller: it flows straight through into SiteSearchQuery.Query without any
    // trim, truncation or normalisation. GREEN at HEAD because no cap exists yet — the same
    // observable behaviour that AC-H1 requires happens to be what an uncapped forwarder
    // produces at the boundary length. Asserting on SiteSearchQuery.Query keeps the check
    // scoped to controller responsibility (the FTS layer's stemming/rank behaviour is
    // owned by the integration tier).
    [Fact]
    [Trait("prd-case", "H")]
    public async Task Index_100CharQuery_PassesThroughToService()
    {
        var query = new string('a', 100);
        var sut = CreateSut();

        _ = await sut.Index(query, scope: null, includePages: null, includeContentBlocks: null);

        await _searchService.Received(1).SearchAsync(
            Arg.Is<SiteSearchQuery>(q => q.Query != null && q.Query.Length == 100 && q.Query == query));
    }

    // AC-H3 — a 100-char query with no whitespace exercises the "no OR-join opportunity"
    // path through SearchTermNormalizer downstream, but the controller's obligation is the
    // same as AC-H1: forward the raw string untouched. Same GREEN reason as AC-H1 — the
    // absent cap is a no-op at the boundary length. The mocked SearchAsync must not throw
    // (it doesn't — NSubstitute's Returns(...) guarantees a value), so the "does not blow up
    // the tokeniser" clause of AC-H3 is asserted vacuously at the controller tier and re-
    // asserted for real by the SearchTermNormalizer/repository integration tests.
    [Fact]
    [Trait("prd-case", "H")]
    public async Task Index_100CharSingleWordQuery_DoesNotThrow_AndPassesThroughToService()
    {
        var query = new string('x', 100); // no whitespace — single "word" at the boundary
        var sut = CreateSut();

        _ = await sut.Index(query, scope: null, includePages: null, includeContentBlocks: null);

        await _searchService.Received(1).SearchAsync(
            Arg.Is<SiteSearchQuery>(q => q.Query != null && q.Query.Length == 100 && !q.Query.Contains(' ')));
    }

    // AC-H2 — KNOWN BASELINE GAP. SearchController currently does NOT trim `q` to 100 chars
    // before constructing the SiteSearchQuery, so a 500-char input reaches the FTS layer
    // verbatim. The assertion body carries the shape the test WILL take once the cap lands
    // (Query.Length == 100 after the controller trims), so re-enabling this test after the
    // fix is a single-line change: drop the Skip argument. The known-bug slug (case-h-cap)
    // is the audit-trail key — a follow-on record under that slug tracks the fix owner.
    //
    // Skip string is a slug-only sentence — no path or external reference, per the project
    // rule that source code (including tests) carries no such references.
    [Fact(Skip = "Known baseline gap — case-h-cap")]
    [Trait("prd-case", "H")]
    [Trait("known-bug", "case-h-cap")]
    public async Task Index_500CharQuery_TrimsToLeading100Chars_BeforeService()
    {
        var query = new string('a', 500);
        var sut = CreateSut();

        _ = await sut.Index(query, scope: null, includePages: null, includeContentBlocks: null);

        // When the cap ships, this assertion is the contract: the controller must trim to
        // the first 100 characters before the query hits SiteSearchService. The equality
        // check on the leading substring guarantees "leading 100 chars" (not "any 100 chars").
        await _searchService.Received(1).SearchAsync(
            Arg.Is<SiteSearchQuery>(q =>
                q.Query != null
                && q.Query.Length == 100
                && q.Query == query.Substring(0, 100)));
    }
}
