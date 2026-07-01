using DfE.CheckPerformanceData.E2ETests.Fixtures;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;

namespace DfE.CheckPerformanceData.E2ETests.Web;

// The guidance pages are [AllowAnonymous] and render entirely from the section manifest
// with defaultText/defaultHtml fallbacks, so they show full structure even with no seeded
// CMS content. These tests assert template-owned structure, not seeded copy.
[Collection("E2E")]
[Trait("Category", "W1")]
public sealed class GuidancePagesTests(PlaywrightFixture fixture) : PageTest
{
    private readonly PlaywrightFixture _fixture = fixture;

    [Fact]
    public async Task GuidanceLanding_Returns200_AndRendersHeadingAndSearch()
    {
        var response = await Page.GotoAsync($"{_fixture.BaseUrl}/guidance");

        Assert.NotNull(response);
        Assert.Equal(200, response!.Status);
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync("CYPMD help and support");
        // The landing search box posts to the shared help search.
        await Expect(Page.Locator("form.cypmd-search[action='/help/search']")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Ks4Page_Returns200_AndRendersTitleAndSideNav()
    {
        var response = await Page.GotoAsync($"{_fixture.BaseUrl}/guidance/2026-ks4-june-checking-exercise");

        Assert.NotNull(response);
        Assert.Equal(200, response!.Status);
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync("How to check your performance measures data: KS4 June 2026");
        await Expect(Page.Locator(".guidance-side-nav")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Ks4Page_RendersTemplateOwnedSectionAnchors()
    {
        await Page.GotoAsync($"{_fixture.BaseUrl}/guidance/2026-ks4-june-checking-exercise");

        // Landmark anchors live on the <section> wrappers (template-owned, never sanitised),
        // so the in-page navigation always has a target.
        await Expect(Page.Locator("section#key-dates")).ToBeVisibleAsync();
        await Expect(Page.Locator("section#pupil-removal-reason")).ToBeVisibleAsync();
        await Expect(Page.Locator("section#get-support")).ToBeVisibleAsync();

        // The full manifest renders many navigable sections.
        Assert.True(await Page.Locator("section.guidance-section").CountAsync() >= 25);
    }
}
