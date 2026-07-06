using DfE.CheckPerformanceData.E2ETests.Fixtures;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;

namespace DfE.CheckPerformanceData.E2ETests.Web;

// The guidance pages used to be rendered by GuidanceController from a section manifest in
// code. That controller was retired on this branch — /guidance and its children now
// resolve through the CMS catch-all against a seeded PageNode tree, so what renders is
// entirely dependent on which content has been imported into the target environment.
//
// The three tests below assert layout details (specific H1 copy, a ".guidance-side-nav"
// element, template-owned section anchors, a >=25 section count) that only hold for the
// old manifest-driven view. Skipping them until the CMS-driven equivalents are written —
// deletion is safer than a rewrite that guesses at the seeded content.
[Collection("E2E")]
[Trait("Category", "W1")]
public sealed class GuidancePagesTests(PlaywrightFixture fixture) : PageTest
{
    private readonly PlaywrightFixture _fixture = fixture;
    private const string SkipReason =
        "Guidance-in-code was retired on the content-page-builder branch; "
        + "these assertions target manifest-owned markup that no longer exists.";

    [Fact(Skip = SkipReason)]
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

    [Fact(Skip = SkipReason)]
    public async Task Ks4Page_Returns200_AndRendersTitleAndSideNav()
    {
        var response = await Page.GotoAsync($"{_fixture.BaseUrl}/guidance/2026-ks4-june-checking-exercise");

        Assert.NotNull(response);
        Assert.Equal(200, response!.Status);
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync("How to check your performance measures data: KS4 June 2026");
        await Expect(Page.Locator(".guidance-side-nav")).ToBeVisibleAsync();
    }

    [Fact(Skip = SkipReason)]
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
