using DfE.CheckPerformanceData.E2ETests.Fixtures;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;

namespace DfE.CheckPerformanceData.E2ETests;

[Collection("E2E")]
public sealed class HarnessSmokeTests(PlaywrightFixture fixture) : PageTest
{
    private readonly PlaywrightFixture _fixture = fixture;

    // --- LoadsHealthcheckPage ---

    [Fact]
    public async Task LoadsHealthcheckPage_Returns200()
    {
        var response = await Page.GotoAsync($"{_fixture.BaseUrl}/healthcheck");

        Assert.NotNull(response);
        Assert.Equal(200, response!.Status);
    }

    // --- GovukFrontendStylesheetLoads ---

    [Fact]
    public async Task GovukFrontendStylesheetLoads_BodyHasGovukTemplateClass()
    {
        await Page.GotoAsync($"{_fixture.BaseUrl}/help");

        await Expect(Page.Locator("body.govuk-template__body")).ToBeVisibleAsync();
    }
}
