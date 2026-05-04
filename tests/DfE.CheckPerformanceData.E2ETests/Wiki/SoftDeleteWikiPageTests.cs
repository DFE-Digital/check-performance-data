using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;

namespace DfE.CheckPerformanceData.E2ETests.Wiki;

[Collection("E2E")]
[Trait("Category", "W2")]
public sealed class SoftDeleteWikiPageTests(PlaywrightFixture fixture) : PageTest, IAsyncLifetime
{
    private readonly PlaywrightFixture _fixture = fixture;
    private readonly List<int> _trackedIds = [];

    public new async Task InitializeAsync() => await base.InitializeAsync();

    public new async Task DisposeAsync()
    {
        foreach (var id in _trackedIds.AsEnumerable().Reverse())
        {
            try
            {
                await SeedHelpers.SoftDeleteWikiPageAsync(_fixture.SeedClient, id);
            }
            catch
            {
                // Page may already have been soft-deleted by the test body — second call is a
                // best-effort cleanup.
            }
        }
        await base.DisposeAsync();
    }

    // --- CreatedPage_AppearsInSearch ---

    [Fact]
    public async Task CreatedPage_AppearsInSearch()
    {
        var queryToken = $"e2eseed{Guid.NewGuid().ToString("N")[..8]}";
        await SeedHelpers.SeedWikiPageAsync(
            _fixture.SeedClient,
            title: $"{queryToken} target",
            body: $"Content for {queryToken}.",
            parentId: null,
            _trackedIds);

        await Page.GotoAsync($"{_fixture.BaseUrl}/help/search?q={queryToken}");

        // The freshly-seeded page must surface in /help/search?q={token}. Search.cshtml renders
        // each result as <li> containing <a class="govuk-link" href="/help/{slug}">…</a>; the
        // slug carries the query token.
        var matchingLinks = Page.Locator($"a.govuk-link[href*=\"{queryToken}\"]");
        await Expect(matchingLinks.First).ToBeVisibleAsync();
    }

    // --- SoftDeletedPage_DoesNotAppearInSearch ---

    [Fact]
    public async Task SoftDeletedPage_DoesNotAppearInSearch()
    {
        var queryToken = $"e2edel{Guid.NewGuid().ToString("N")[..8]}";
        var id = await SeedHelpers.SeedWikiPageAsync(
            _fixture.SeedClient,
            title: $"{queryToken} soon-deleted",
            body: $"Content for {queryToken}, will be soft-deleted before search.",
            parentId: null,
            _trackedIds);

        await SeedHelpers.SoftDeleteWikiPageAsync(_fixture.SeedClient, id);

        await Page.GotoAsync($"{_fixture.BaseUrl}/help/search?q={queryToken}");

        // The soft-delete EF query filter (HasQueryFilter(w => !w.IsDeleted)) MUST hide this
        // page from the search results. Zero matching anchors is the contract.
        await Expect(Page.Locator($"a.govuk-link[href*=\"{queryToken}\"]")).ToHaveCountAsync(0);
    }
}
