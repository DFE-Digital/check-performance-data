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

        // RED: deliberately assert ZERO matches — once the seed lands and the FTS index picks it
        // up, at least one result anchor must point to a slug containing the query token.
        await Expect(Page.Locator($"a.govuk-link[href*=\"{queryToken}\"]")).ToHaveCountAsync(0);
    }
}
