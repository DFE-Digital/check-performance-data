using DfE.CheckPerformanceData.E2ETests.Fixtures;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;

namespace DfE.CheckPerformanceData.E2ETests.Web;

// The Beta phase banner's "feedback" link opens the feedback survey in a new tab. The anchor
// goes through /feedback-link (which records the click) and that redirects to the survey, an
// external Microsoft Forms page. The redirect itself is asserted at HTTP level because
// Playwright route handlers do not see redirected requests, and the popup's navigation is
// stubbed so the test never depends on the external host.
[Collection("E2E")]
public sealed class FeedbackLinkTests(PlaywrightFixture fixture) : PageTest
{
    private const string SurveyUrl = "https://forms.cloud.microsoft/e/NtJTefhXHz";
    private readonly PlaywrightFixture _fixture = fixture;

    [Fact]
    public async Task PhaseBannerFeedbackLink_OpensANewTab()
    {
        // Stub the popup's own navigation so the test never leaves localhost. Playwright route
        // handlers are not invoked for redirected requests, so the survey host cannot be stubbed;
        // the redirect itself is asserted separately below.
        await Page.Context.RouteAsync("**/feedback-link", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "text/html",
            Body = "<html><body><h1>Stub</h1></body></html>",
        }));

        await Page.GotoAsync($"{_fixture.BaseUrl}/contact");

        var link = Page.GetByRole(AriaRole.Link, new() { Name = "feedback (opens in new tab)" });
        await Expect(link).ToBeVisibleAsync();
        await Expect(link).ToHaveAttributeAsync("target", "_blank");
        await Expect(link).ToHaveAttributeAsync("rel", "noopener");

        var popup = await Page.RunAndWaitForPopupAsync(() => link.ClickAsync());
        await popup.WaitForLoadStateAsync();

        Assert.Equal($"{_fixture.BaseUrl}/feedback-link", popup.Url);
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/contact$"));
    }

    [Fact]
    public async Task FeedbackLink_RedirectsToTheSurvey_WithoutAReferrer()
    {
        var response = await Page.APIRequest.GetAsync($"{_fixture.BaseUrl}/feedback-link", new() { MaxRedirects = 0 });
        Assert.Equal(302, response.Status);
        Assert.Equal(SurveyUrl, response.Headers["location"]);
        Assert.Equal("no-referrer", response.Headers["referrer-policy"]);
    }
}
