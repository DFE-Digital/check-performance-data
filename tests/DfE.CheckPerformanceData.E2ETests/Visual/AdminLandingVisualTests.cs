using System.Runtime.InteropServices;
using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;

namespace DfE.CheckPerformanceData.E2ETests.Visual;

[Collection("E2E")]
[Trait("Category", "W4")]
[Trait("Category", "VisualRegression")]
public sealed class AdminLandingVisualTests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    public override BrowserNewContextOptions ContextOptions() =>
        new() { ViewportSize = new ViewportSize { Width = 1280, Height = 720 } };

    // --- AdminLandingPage_MatchesSnapshot ---

    [SkippableFact]
    public async Task AdminLandingPage_MatchesSnapshot()
    {
        Skip.IfNot(VisualRegressionSwitch.Enabled, VisualRegressionSwitch.SkipReason);

        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Linux),
            "Visual regression Linux-only");

        try
        {
            // Two-step: capture the admin cookie via the dev impersonation endpoint,
            // then mirror it into the Playwright BrowserContext. SeedingPageTest only
            // seeds the cookie that was in TestHttpClients.ImpersonationCookieHeader
            // at InitializeAsync time, which is whatever the prior test left (likely
            // the fixture-level editor cookie or null on a clean first run). Without
            // this mirror, Page.GotoAsync("/admin") would carry no admin cookie and
            // either 302 to DSI sign-in (clean run) or 302 to AccessDenied (editor
            // cookie present, since admin and editor roles are orthogonal).
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

            await Page.GotoAsync($"{Fixture.BaseUrl}/admin");
            await Page.StabiliseAsync();

            await Page.MatchSnapshotAsync("admin-landing-page.png");
        }
        finally
        {
            // Restore the fixture-level editor cookie so subsequent tests in the
            // collection that rely on the editor impersonation still see an editor
            // principal. Admin and editor roles are orthogonal.
            await AuthHelpers.ImpersonateAsEditorAsync(Fixture);
        }
    }
}
