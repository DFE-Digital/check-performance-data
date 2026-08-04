// Cross-browser smoke scaffold. Loads the app root in WebKit and Firefox in addition to
// Chromium (which the rest of the suite already covers via PageTest) and asserts a 200.
// This is a scaffold, not a full-suite alternative: the goal is to catch WebKit- or
// Firefox-specific regressions in the layout without the cost of running every hover /
// tooltip / click test three times.
//
// PREREQUISITE: WebKit and Firefox browsers must be installed on the runner:
//     pwsh tests/DfE.CheckPerformanceData.E2ETests/bin/Debug/net10.0/playwright.ps1 install webkit firefox
// The [SkippableFact] guard skips cleanly when the browser binary can't be launched,
// so the test does NOT block a CI run that ships only Chromium.
//
// Runs Linux-only for the same font-metrics reason the other Playwright tests use;
// visual dimensions on other OSes drift enough that hard equality on the 200 status
// (which is all we assert here) would still pass, but keeping the guard consistent
// with the rest of the suite avoids surprise skips on Windows dev boxes.
using System.Runtime.InteropServices;
using DfE.CheckPerformanceData.E2ETests.Fixtures;
using Microsoft.Playwright;

namespace DfE.CheckPerformanceData.E2ETests;

[Collection("E2E")]
[Trait("Category", "W0")]
public sealed class CrossBrowserSmokeTests
{
    private readonly PlaywrightFixture _fixture;

    public CrossBrowserSmokeTests(PlaywrightFixture fixture) => _fixture = fixture;

    [SkippableTheory]
    [InlineData("webkit")]
    [InlineData("firefox")]
    public async Task LoadsApplicationRoot_Returns200_InTargetBrowser(string browserKey)
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Linux),
            "Playwright browser test Linux-only");

        using var playwright = await Playwright.CreateAsync();

        var browserType = browserKey switch
        {
            "webkit" => playwright.Webkit,
            "firefox" => playwright.Firefox,
            _ => throw new ArgumentOutOfRangeException(nameof(browserKey), browserKey, null),
        };

        IBrowser? browser = null;
        try
        {
            browser = await browserType.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,
            });
        }
        catch (PlaywrightException ex)
        {
            // Missing browser binary or dependency — skip rather than fail, so a CI
            // runner without webkit/firefox installed still passes cleanly. The full
            // fix is to run `pwsh playwright.ps1 install webkit firefox` (see file
            // header).
            Skip.If(true,
                $"Skipping {browserKey} smoke — browser could not launch ({ex.Message}). " +
                $"Install with: pwsh playwright.ps1 install {browserKey}");
        }

        try
        {
            var context = await browser!.NewContextAsync();
            var page = await context.NewPageAsync();
            var response = await page.GotoAsync($"{_fixture.BaseUrl}/");
            Assert.NotNull(response);
            Assert.Equal(200, response!.Status);
        }
        finally
        {
            if (browser is not null)
            {
                await browser.CloseAsync();
            }
        }
    }
}
