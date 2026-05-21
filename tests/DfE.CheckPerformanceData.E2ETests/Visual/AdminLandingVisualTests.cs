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
            // collection that rely on the editor impersonation (e.g. WikiCrudTests'
            // antiforgery scrape of /help?edit) still see an editor principal.
            // Admin and editor roles are orthogonal — leaving the admin cookie set
            // would render /help?edit as a read-only page with no antiforgery form.
            await AuthHelpers.ImpersonateAsEditorAsync(Fixture);
        }
    }

    // --- DeletedPagesPage_MatchesSnapshot ---

    [SkippableFact]
    public async Task DeletedPagesPage_MatchesSnapshot()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Linux),
            "Visual regression Linux-only");

        // The /help/deleted screen renders a govuk-table only when there's at least
        // one soft-deleted page; seed-then-soft-delete a single page so the table
        // (with the new "Actions" column header + Restore + Delete-permanently
        // button-group) is captured by the snapshot. The seeded page is tracked
        // and cleaned up by SeedingPageTest.DisposeAsync. The fixture is already
        // authenticated as editor (SeedingPageTest mirrors the impersonation
        // cookie into the BrowserContext), and editors can both view /help/deleted
        // and soft-delete pages.
        var seededId = await SeedHelpers.SeedWikiPageAsync(
            Fixture.SeedClient,
            title: "vr-deleted-fixture",
            body: "<p>Visual regression fixture page; will be soft-deleted before snapshot.</p>",
            parentId: null,
            TrackedIds);
        await SeedHelpers.SoftDeleteWikiPageAsync(Fixture.SeedClient, seededId);

        await Page.GotoAsync($"{Fixture.BaseUrl}/help/deleted");
        await Page.StabiliseAsync();

        // Viewport-only capture rather than full-page: prior soft-deleted fixtures
        // from sibling E2E tests can leave additional rows in the table, which
        // would change the full-page height between runs and break the snapshot
        // dimension comparison. The viewport screenshot (1280x720) is enough to
        // pin the "Actions" header + first row layout — which is the contract this
        // snapshot is asserting.
        await Page.MatchSnapshotAsync("admin-deleted-pages-page.png", fullPage: false);
    }
}
