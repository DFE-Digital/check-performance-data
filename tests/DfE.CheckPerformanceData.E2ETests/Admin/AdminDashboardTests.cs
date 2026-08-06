using System.Runtime.InteropServices;
using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;

namespace DfE.CheckPerformanceData.E2ETests.Admin;

// End-to-end coverage for the admin engagement dashboard (/admin/dashboard, PBI 288143).
// The seeded environment always carries at least one open checking window, so the tests
// assert the data path: window select + nine metric tiles + the stop-auto-refresh button,
// which is the WCAG 2.2 SC 2.2.2 mitigation (page auto-reloads when the cache expires, so
// users must be able to stop it). The button is hidden until the script schedules a
// reload, then clicking it cancels the reload and announces via the role=status region.
[Collection("E2E")]
[Trait("Category", "W4")]
public sealed class AdminDashboardTests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    public override BrowserNewContextOptions ContextOptions() =>
        new() { ViewportSize = new ViewportSize { Width = 1440, Height = 900 } };

    [SkippableFact]
    public async Task Dashboard_RendersWindowSelectAndNineNonClickableTiles()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Linux),
            "Playwright browser test Linux-only");

        try
        {
            var adminCookie = await AuthHelpers.ImpersonateAsAdminAsync(Fixture);
            AttachCookieToContext(adminCookie);

            var response = await Page.GotoAsync($"{Fixture.BaseUrl}/admin/dashboard");
            Assert.NotNull(response);
            Assert.Equal(200, response!.Status);

            // Open-window selector with at least one option (dev seeding guarantees one).
            var select = Page.Locator("select#windowId");
            await Expect(select).ToBeVisibleAsync();
            Assert.True(await select.Locator("option").CountAsync() >= 1,
                "Expected at least one open window in the selector.");

            // Nine metric tiles across the two sections; the spec settled on metrics only,
            // so no tile may contain a link.
            await Page.Locator(".dash-tiles").First.WaitForAsync(
                new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
            Assert.Equal(9, await Page.Locator(".dash-tile").CountAsync());
            Assert.Equal(0, await Page.Locator(".dash-tile a").CountAsync());

            // Every tile renders an integer value and a label.
            var values = Page.Locator(".dash-tile .dash-tile__value");
            Assert.Equal(9, await values.CountAsync());
            for (var i = 0; i < 9; i++)
            {
                var text = (await values.Nth(i).InnerTextAsync()).Trim();
                Assert.Matches(new System.Text.RegularExpressions.Regex(@"^[0-9,]+$"), text);
            }
            Assert.Equal(9, await Page.Locator(".dash-tile .dash-tile__label").CountAsync());

            await Page.StabiliseAsync();
            await SaveScreenshotAsync("admin-dashboard.png");
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(Fixture);
        }
    }

    [SkippableFact]
    public async Task Dashboard_StopAutoRefresh_RevealsCancelsAndAnnounces()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Linux),
            "Playwright browser test Linux-only");

        try
        {
            var adminCookie = await AuthHelpers.ImpersonateAsAdminAsync(Fixture);
            AttachCookieToContext(adminCookie);

            var response = await Page.GotoAsync($"{Fixture.BaseUrl}/admin/dashboard");
            Assert.NotNull(response);
            Assert.Equal(200, response!.Status);

            // The button ships hidden and is revealed only after the script has actually
            // scheduled a reload — its visibility is itself the assertion that the
            // auto-refresh got scheduled.
            var stop = Page.Locator("[data-stop-auto-refresh]");
            await Expect(stop).ToBeVisibleAsync();

            // Clicking cancels the reload, re-hides the button and updates the live region.
            await stop.ClickAsync();
            await Expect(stop).ToBeHiddenAsync();
            await Expect(Page.Locator("[data-auto-refresh-status]")).ToHaveTextAsync(
                "Automatic refresh is off. Reload the page to see newer figures.");

            await Page.StabiliseAsync();
            await SaveScreenshotAsync("admin-dashboard-stop-refresh.png");
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(Fixture);
        }
    }

    // --- Helpers ---

    private void AttachCookieToContext(string? cookieHeader)
    {
        if (string.IsNullOrEmpty(cookieHeader)) return;
        var equalsIndex = cookieHeader.IndexOf('=');
        if (equalsIndex <= 0) return;

        Context.AddCookiesAsync([new Cookie
        {
            Name = cookieHeader[..equalsIndex],
            Value = cookieHeader[(equalsIndex + 1)..],
            Url = Fixture.BaseUrl
        }]).GetAwaiter().GetResult();
    }

    // Screenshots — path-safe under the test project so a reviewer can
    // browse them from git. Not versus a baseline; not a visual-regression assertion.
    private Task SaveScreenshotAsync(string filename) => SaveScreenshotAsync(Page, filename);

    private static async Task SaveScreenshotAsync(IPage page, string filename)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Snapshots", "search-ux", "admin-dashboard");
        Directory.CreateDirectory(dir);
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(dir, filename),
            FullPage = true,
        });
    }
}
