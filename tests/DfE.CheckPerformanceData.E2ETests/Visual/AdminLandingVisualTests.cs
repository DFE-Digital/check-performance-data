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
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Linux),
            "Visual regression Linux-only");

        try
        {
            await AuthHelpers.ImpersonateAsAdminAsync(Fixture);

            await Page.GotoAsync($"{Fixture.BaseUrl}/admin");
            await Page.StabiliseAsync();

            await Page.MatchSnapshotAsync("admin-landing-page.png");
        }
        finally
        {
            // Restore the fixture-level editor cookie so subsequent tests in the
            // collection that rely on the editor impersonation (e.g. WikiCrudTests'
            // antiforgery scrape of /help?edit) still see an editor principal.
            // Admin and editor roles are orthogonal — leaving the admin cookie set
            // would render /help?edit as a read-only page with no antiforgery form.
            await AuthHelpers.ImpersonateAsEditorAsync(Fixture);
        }
    }
}
