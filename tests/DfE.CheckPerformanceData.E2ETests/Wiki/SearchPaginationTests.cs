using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;

namespace DfE.CheckPerformanceData.E2ETests.Wiki;

// Reader-path pagination smoke over /search. Seeds a fresh 25-page corpus under a
// unique token so a search for the token guarantees exactly those hits, then walks
// page 1 → page 2 through the GDS paginator and asserts the URL + result set change.
// 25 hits at the shared 20-per-page default yields exactly two pages — no admin-
// setting override needed.
//
// Selector note: /search renders results into <ul class="... cypmd-search-results">
// (the class lives on the ul itself), whereas the widget wraps its ul in
// <div class="cypmd-search-results">. A descendant selector — ".cypmd-search-results
// li h3 a" — matches both without needing an intermediate ul.govuk-list step.
[Collection("E2E")]
[Trait("Category", "W1")]
public sealed class SearchPaginationTests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    // Track seeded pages for teardown so a failing test doesn't leave orphans that
    // future test runs would then collide with.
    private readonly List<Guid> _createdPages = [];

    // Overrides SeedingPageTest.DisposeAsync (new virtual against PageTest's non-virtual
    // Playwright teardown). Matches the ResultsWidgetE2ETests pattern.
    public override async Task DisposeAsync()
    {
        foreach (var id in _createdPages)
        {
            await CmsSeedHelpers.TryDeletePageAsync(Fixture.SeedClient, id);
        }
        await base.DisposeAsync();
    }

    // Seeds `count` PageNodes under /help whose titles all contain a fresh unique
    // token, then returns the token. A search for that token is guaranteed to hit
    // exactly those pages — makes the pagination test independent of whatever content
    // the deployed env happens to carry. Each seeded page's Title contributes to
    // PageNode.SearchVector at weight B.
    private async Task<string> SeedSearchableFixturesAsync(int count)
    {
        // Lowercase hex chunk: tsvector-safe (no stopword collision) and short enough
        // to keep the test title readable.
        var token = "cypde2e" + Guid.NewGuid().ToString("N")[..12].ToLowerInvariant();
        for (int i = 0; i < count; i++)
        {
            var segment = $"e2e-fixture-{i}-{Guid.NewGuid():N}";
            var id = await CmsSeedHelpers.CreatePageNodeAsync(
                Fixture.SeedClient,
                parentId: CmsSeedHelpers.HelpRootId,
                pageType: "content",
                segment: segment,
                title: $"E2E fixture {token} number {i}");
            _createdPages.Add(id);
            await CmsSeedHelpers.PublishDraftAsync(Fixture.SeedClient, id);
        }
        return token;
    }

    // Reader-path pagination smoke over /search. Seeds 25 hits and lets the shared
    // 20-per-page default drive the paginator into a 2-page state, navigates the
    // pager, and asserts the URL + hit set both change.
    [Fact]
    [Trait("search-case", "pagination")]
    public async Task Search_PaginatesAcrossMultiplePages_ForHighVolumeQuery()
    {
        var token = await SeedSearchableFixturesAsync(count: 25);

        await Page.GotoAsync($"{Fixture.BaseUrl}/search?q={token}");

        // Pagination is only rendered when TotalPages > 1 — its presence is the
        // proof that 25 seeded hits crossed the 20-per-page threshold.
        await Expect(Page.Locator(".govuk-pagination__list")).ToBeVisibleAsync();

        var page1Titles = await Page.Locator(".cypmd-search-results li h3 a")
            .AllInnerTextsAsync();
        Assert.NotEmpty(page1Titles);

        // Click the numbered "2" pager link (its href carries page=2).
        await Page.Locator(".govuk-pagination__link[href*='page=2']").First.ClickAsync();
        await Page.WaitForURLAsync("**page=2**");

        var page2Titles = await Page.Locator(".cypmd-search-results li h3 a")
            .AllInnerTextsAsync();
        Assert.NotEmpty(page2Titles);

        // Page 2 must not be identical to page 1 (otherwise the pager didn't page).
        Assert.NotEqual(page1Titles, page2Titles);

        // Previous appears on page 2 — back-nav is available.
        await Expect(Page.Locator(".govuk-pagination__prev a")).ToBeVisibleAsync();
    }
}
