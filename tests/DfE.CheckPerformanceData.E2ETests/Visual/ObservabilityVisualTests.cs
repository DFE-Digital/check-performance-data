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
[Trait("Category", "W4")]
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

            // Purge all demo / E2E-seeded traffic so the dashboard renders its deterministic empty
            // state. Other tests in the E2E collection drive metric events and seed dead-letters
            // (DEV-/SEED-/e2e-/… prefixes) that otherwise leave the matrix, tiles, charts and health
            // strip in an order-dependent state and flake this full-page snapshot. The admin
            // impersonation cookie set above is auto-attached by TestHttpClients.SendAsync.
            using (var purge = new HttpRequestMessage(HttpMethod.Post, $"{Fixture.BaseUrl}/dev/uat/purge-demo"))
            {
                await TestHttpClients.SendAsync(purge);
            }

            await Page.GotoAsync($"{Fixture.BaseUrl}/admin/observability");
            await Page.StabiliseAsync();

            // After the purge the only volatile region is the "Refreshed at <timestamp>" hint, which
            // changes on every render; neutralise it so the snapshot is byte-stable.
            await Page.EvaluateAsync(@"() => {
                document.querySelectorAll('p.govuk-hint').forEach(p => {
                    if (p.textContent && p.textContent.indexOf('Refreshed at') === 0) {
                        p.textContent = 'Refreshed at [stable].';
                    }
                });
            }");
            await Page.EvaluateAsync(
                "() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");

            await Page.MatchSnapshotAsync("observability-dashboard-page.png");
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(Fixture);
        }
    }
}
