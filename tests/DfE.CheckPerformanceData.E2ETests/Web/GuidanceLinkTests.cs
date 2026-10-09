using DfE.CheckPerformanceData.E2ETests.Fixtures;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;

namespace DfE.CheckPerformanceData.E2ETests.Web;

// AB#306103: the service-navigation "Guidance" link opens the guidance in a new tab and leaves
// the page the user was on where it was. Same-origin, so nothing is stubbed — the popup really
// loads /guidance. The footer and home-card anchors share the same attributes and are pinned at
// view-source tier (GuidanceLinksViewSourceTests); this fact proves the browser behaviour once.
[Trait("Category", "FullRegression")]
[Collection("E2E")]
public sealed class GuidanceLinkTests(PlaywrightFixture fixture) : PageTest
{
    private readonly PlaywrightFixture _fixture = fixture;

    [Fact]
    public async Task ServiceNavigationGuidanceLink_OpensTheGuidanceInANewTab()
    {
        // The home page is anonymous and renders the shared layout, so no sign-in is needed.
        await Page.GotoAsync($"{_fixture.BaseUrl}/");

        var menu = Page.GetByRole(AriaRole.Navigation, new() { Name = "Menu" });
        var link = menu.GetByRole(AriaRole.Link, new() { Name = "Guidance (opens in new tab)" });
        await Expect(link).ToBeVisibleAsync();
        await Expect(link).ToHaveAttributeAsync("target", "_blank");
        await Expect(link).ToHaveAttributeAsync("rel", "noopener");

        var popup = await Page.RunAndWaitForPopupAsync(() => link.ClickAsync());
        await popup.WaitForLoadStateAsync();

        Assert.Equal($"{_fixture.BaseUrl}/guidance", popup.Url);
        // The original tab is untouched.
        Assert.Equal($"{_fixture.BaseUrl}/", Page.Url);
    }
}
