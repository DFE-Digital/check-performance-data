using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;

namespace DfE.CheckPerformanceData.E2ETests.Web;

[Collection("E2E")]
public sealed class ContactUsTests(PlaywrightFixture fixture) : PageTest
{
    private readonly PlaywrightFixture _fixture = fixture;

    [Fact]
    public async Task Anonymous_shows_reduced_list_and_contact_fields()
    {
        await Page.GotoAsync($"{_fixture.BaseUrl}/contact");

        await Expect(Page.GetByLabel("Your name")).ToBeVisibleAsync();
        await Expect(Page.GetByLabel("Your email address")).ToBeVisibleAsync();

        var radios = Page.Locator("input[name='EnquiryType']");
        await Expect(radios).ToHaveCountAsync(3);
    }

    [Fact]
    public async Task NoSelection_shows_error_summary()
    {
        await Page.GotoAsync($"{_fixture.BaseUrl}/contact");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Submit" }).ClickAsync();
        await Page.StabiliseAsync();

        await Expect(Page.Locator(".govuk-error-summary")).ToContainTextAsync("There is a problem");
    }

    [Fact]
    public async Task ValidSubmit_redirects_and_shows_no_details_banner()
    {
        await Page.GotoAsync($"{_fixture.BaseUrl}/contact");
        await Page.Locator("input[name='EnquiryType'][value='technical-problem']").CheckAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Submit" }).ClickAsync();
        await Page.StabiliseAsync();

        // Anonymous direct-GET has no returnUrl, so it falls back to the guidance landing.
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/guidance$"));
        await Expect(Page.Locator(".govuk-notification-banner"))
            .ToContainTextAsync("We haven't recorded any details");
    }

    [Fact]
    public async Task SignedIn_shows_full_list_and_hides_contact_fields()
    {
        // Impersonate as editor (an authenticated principal) so the signed-in variant renders,
        // then attach the cookie to the browser context before navigating.
        var cookie = await AuthHelpers.ImpersonateAsEditorAsync(_fixture);
        Assert.NotNull(cookie);
        var kv = cookie!.Split('=', 2);
        await Page.Context.AddCookiesAsync([new Cookie { Name = kv[0], Value = kv[1], Url = _fixture.BaseUrl }]);

        await Page.GotoAsync($"{_fixture.BaseUrl}/contact");

        var radios = Page.Locator("input[name='EnquiryType']");
        await Expect(radios).ToHaveCountAsync(4);
        await Expect(Page.GetByLabel("Your name")).ToHaveCountAsync(0);
    }
}
