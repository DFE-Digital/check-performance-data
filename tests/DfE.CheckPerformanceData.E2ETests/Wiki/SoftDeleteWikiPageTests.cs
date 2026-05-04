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

        // RED: assert the soft-deleted page IS visible — if this passes, the EF query filter
        //      WikiPageConfiguration.HasQueryFilter(w => !w.IsDeleted) is broken on the search
        //      code path. The product behaviour is correct (filter applied), so this assertion
        //      fails and we flip it to ToHaveCountAsync(0) for GREEN.
        await Expect(Page.Locator($"a.govuk-link[href*=\"{queryToken}\"]").First).ToBeVisibleAsync();
    }
}
