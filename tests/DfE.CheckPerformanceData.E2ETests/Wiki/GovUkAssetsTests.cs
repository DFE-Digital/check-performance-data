using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;
using xRetry;

namespace DfE.CheckPerformanceData.E2ETests.Wiki;

[Collection("E2E")]
[Trait("Category", "W1")]
public sealed class GovUkAssetsTests(PlaywrightFixture fixture) : PageTest
{
    private readonly PlaywrightFixture _fixture = fixture;

    // --- JsEnabledStylesheetsScripts_Return200 ---

    [RetryFact(2)]
    public async Task JsEnabledStylesheetsScripts_Return200()
    {
        var govukResponses = new List<IResponse>();

        Page.Response += (_, response) =>
        {
            if (response.Url.Contains("govuk-frontend", StringComparison.OrdinalIgnoreCase))
                govukResponses.Add(response);
        };

        await Page.GotoAsync($"{_fixture.BaseUrl}/help");
        await Page.StabiliseAsync();

        await Expect(Page.Locator("body.govuk-template__body.js-enabled")).ToBeVisibleAsync();

        Assert.NotEmpty(govukResponses);
        foreach (var r in govukResponses)
        {
            Assert.True(r.Status >= 200 && r.Status < 400,
                $"GOV.UK Frontend asset {r.Url} returned {r.Status}");
        }
    }
}
