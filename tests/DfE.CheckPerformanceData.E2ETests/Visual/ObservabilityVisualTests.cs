using System.Runtime.InteropServices;
using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;

namespace DfE.CheckPerformanceData.E2ETests.Visual;

// Visual-regression cover for the observability dashboard, captured in the deterministic
// reduced-motion / paused state (NEVER mid-animation): forcing reduced-motion means the board's
// tokens snap and the skeleton + textual parallel render without any in-flight transform, so the
// snapshot is stable across runs. StabiliseAsync additionally strips animation/transition before
// the capture.
[Collection("E2E")]
[Trait("Category", "VisualRegression")]
public sealed class ObservabilityVisualTests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    public override BrowserNewContextOptions ContextOptions() =>
        new()
        {
            ViewportSize = new ViewportSize { Width = 1280, Height = 720 },
            ReducedMotion = ReducedMotion.Reduce,
        };

    [SkippableFact]
    public async Task DashboardPage_MatchesSnapshot()
    {
        Skip.IfNot(VisualRegressionSwitch.Enabled, VisualRegressionSwitch.SkipReason);

        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Linux),
            "Visual regression Linux-only");

        try
        {
            var adminCookie = await AuthHelpers.ImpersonateAsAdminAsync(Fixture);
            if (!string.IsNullOrEmpty(adminCookie))
            {
                var equalsIndex = adminCookie.IndexOf('=');
                if (equalsIndex > 0)
                {
                    await Context.AddCookiesAsync([new Cookie
                    {
                        Name = adminCookie[..equalsIndex],
                        Value = adminCookie[(equalsIndex + 1)..],
                        Url = Fixture.BaseUrl
                    }]);
                }
            }

            await Page.GotoAsync($"{Fixture.BaseUrl}/admin/observability");
            await Page.StabiliseAsync();

            await Page.MatchSnapshotAsync("observability-dashboard-page.png");
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(Fixture);
        }
    }
}
