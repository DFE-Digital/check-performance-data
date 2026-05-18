using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;

namespace DfE.CheckPerformanceData.E2ETests.Wiki;

[Collection("E2E")]
[Trait("Category", "W1")]
public sealed class WikiNavigationTests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    private string _parentTitle = "";

    protected override async Task SeedAsync()
    {
        var run = Guid.NewGuid().ToString("N");
        _parentTitle = $"e2e-{run}-Parent";
        var childTitle = $"e2e-{run}-Child";

        var parentId = await SeedHelpers.SeedWikiPageAsync(
            Fixture.SeedClient, _parentTitle, "Parent body content.", parentId: null, TrackedIds);
        await SeedHelpers.SeedWikiPageAsync(
            Fixture.SeedClient, childTitle, "Child body content.", parentId: parentId, TrackedIds);
    }

    // --- HelpIndex_RendersNavigationTree ---

    [Fact]
    public async Task HelpIndex_RendersNavigationTree()
    {
        await Page.GotoAsync($"{Fixture.BaseUrl}/help");

        await Expect(Page.Locator("aside.wiki-sidebar")).ToBeVisibleAsync();
        await Expect(Page.Locator("aside.wiki-sidebar ul.tv.tv-root")).ToBeVisibleAsync();
        await Expect(Page.Locator("aside.wiki-sidebar").GetByText(_parentTitle)).ToBeVisibleAsync();
    }
}
