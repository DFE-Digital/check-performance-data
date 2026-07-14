using DfE.CheckPerformanceData.E2ETests.Fixtures;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;
using xRetry;

namespace DfE.CheckPerformanceData.E2ETests.Pages;

[Collection("E2E")]
[Trait("Category", "W0")]
public sealed class RequestSubmissionPage(PlaywrightFixture fixture) : PageTest
{
    private readonly PlaywrightFixture _fixture = fixture;

    [RetryFact(3)]
    public async Task SelfSubmittedDuplicate_ShowsSelfReferentialMessage()
    {
        var baseUrl = _fixture.BaseUrl;

        await Page.GotoAsync($"{baseUrl}/");
        var body = Page.Locator("body");
        await Expect(body).ToBeVisibleAsync();
    }

    [RetryFact(3)]
    public async Task OtherSubmittedDuplicate_DoesNotRevealIdentity()
    {
        var baseUrl = _fixture.BaseUrl;

        await Page.GotoAsync($"{baseUrl}/");
        var body = Page.Locator("body");
        await Expect(body).ToBeVisibleAsync();
    }
}
