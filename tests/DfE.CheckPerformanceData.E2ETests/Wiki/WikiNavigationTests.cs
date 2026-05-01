using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;

namespace DfE.CheckPerformanceData.E2ETests.Wiki;

[Collection("E2E")]
[Trait("Category", "W1")]
public sealed class WikiNavigationTests(PlaywrightFixture fixture) : PageTest, IAsyncLifetime
{
    private readonly PlaywrightFixture _fixture = fixture;
    private readonly List<int> _trackedIds = [];
    private string _parentTitle = "";
    private string _childTitle = "";

    public new async Task InitializeAsync()
    {
        await base.InitializeAsync();

        var run = Guid.NewGuid().ToString("N");
        _parentTitle = $"e2e-{run}-Parent";
        _childTitle = $"e2e-{run}-Child";

        var parentId = await SeedHelpers.SeedWikiPageAsync(
            _fixture.SeedClient, _parentTitle, "Parent body content.", parentId: null, _trackedIds);
        await SeedHelpers.SeedWikiPageAsync(
            _fixture.SeedClient, _childTitle, "Child body content.", parentId: parentId, _trackedIds);
    }

    public new async Task DisposeAsync()
    {
        // Delete child before parent for FK ordering. SeedHelpers appends in seed order
        // (parent first, then child), so reverse the list.
        foreach (var id in _trackedIds.AsEnumerable().Reverse())
            await SeedHelpers.SoftDeleteWikiPageAsync(_fixture.SeedClient, id);
        await base.DisposeAsync();
    }

    // --- HelpIndex_RendersNavigationTree ---

    [Fact]
    public async Task HelpIndex_RendersNavigationTree()
    {
        await Page.GotoAsync($"{_fixture.BaseUrl}/help");

        await Expect(Page.Locator("aside.wiki-sidebar")).ToBeVisibleAsync();
        await Expect(Page.Locator("aside.wiki-sidebar ul.tv.tv-root")).ToBeVisibleAsync();
        await Expect(Page.Locator("aside.wiki-sidebar").GetByText(_parentTitle)).ToBeVisibleAsync();
    }

    // --- ChildPage_ShowsBreadcrumbs: DEFERRED 2026-05-01 to Phase 2.3 (Wiki Breadcrumbs).
    //     Do NOT implement this test in the W1 plan — the product never shipped a breadcrumb
    //     component. Phase 2.3 will ship the GDS govuk-breadcrumbs component + parent-chain
    //     DTO surface, then the test goes green there. ---
}
