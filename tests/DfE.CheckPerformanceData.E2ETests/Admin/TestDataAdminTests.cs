using System.Runtime.InteropServices;
using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;

namespace DfE.CheckPerformanceData.E2ETests.Admin;

// End-to-end coverage for the new Test data sub-group under System administration:
//   * The group heading + both child tiles (Seed sample CMS pages / Seed sample search data)
//     render on the /admin landing under the System administration section.
//   * The Seed sample search data form loads with all five presets + explanatory copy +
//     Seed data button.
//   * Submitting the form with the Last-24-hours preset:
//       - produces a success banner with a link back to /admin/Search/
//       - reports non-zero seeded numbers
//       - moves the dashboard tiles above the preset default (500) — proves the seeder
//         actually landed events.
//
// Runs against a live container; Linux-only per the same font-metrics constraint the
// round-3 + round-7 shots use. Screenshots land under Snapshots/search-ux/round8/.
[Collection("E2E")]
[Trait("Category", "W4")]
public sealed class TestDataAdminTests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    public override BrowserNewContextOptions ContextOptions() =>
        new() { ViewportSize = new ViewportSize { Width = 1440, Height = 900 } };

    // --- /admin landing shows Test data group with two child tiles ---

    [SkippableFact]
    public async Task AdminLanding_TestDataGroup_ShowsBothChildTiles()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Linux),
            "Playwright browser test Linux-only");

        try
        {
            var adminCookie = await AuthHelpers.ImpersonateAsAdminAsync(Fixture);
            AttachCookieToContext(adminCookie);

            var landing = await Page.GotoAsync($"{Fixture.BaseUrl}/admin/");
            Assert.NotNull(landing);
            Assert.Equal(200, landing!.Status);

            // The Test data sub-group renders nested inside the System administration section
            // via the recursive _AdminLandingTile partial; container headings are h3 with no id
            // attribute so we locate by tag + text.
            var systemAdminSection = Page.Locator("section[aria-labelledby=\"group-system-admin\"]");
            await Expect(systemAdminSection).ToBeVisibleAsync();
            var testDataHeading = systemAdminSection.Locator("h3", new LocatorLocatorOptions
            {
                HasTextString = "Test data"
            });
            await Expect(testDataHeading).ToBeVisibleAsync();

            // Both child tiles present and pointing at the right URLs. The nav sidebar also
            // renders these links, so we count-assert instead of expecting a single visible
            // element (Playwright strict-mode balks on multi-matches with ToBeVisibleAsync).
            var cmsPagesForm = Page.Locator("form[action=\"/admin/pages/sample-seed\"]");
            Assert.True(await cmsPagesForm.CountAsync() > 0,
                "Expected the Seed sample CMS pages tile form under /admin/pages/sample-seed.");
            var seedSearchLinks = Page.Locator("a[href=\"/admin/test-data/sample-search-data\"]");
            Assert.True(await seedSearchLinks.CountAsync() > 0,
                "Expected at least one anchor pointing at the new seed-sample-search-data page.");

            // Old title must not appear anywhere on the landing.
            var bodyText = await Page.Locator("body").InnerTextAsync();
            Assert.DoesNotContain("Seed sample pages", bodyText);
            Assert.Contains("Seed sample CMS pages", bodyText);
            Assert.Contains("Seed sample search data", bodyText);

            await Page.StabiliseAsync();
            await SaveScreenshotAsync("admin-nav-test-data-group.png");
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(Fixture);
        }
    }

    // --- Seed sample search data form loads with all five presets + copy + submit button ---

    [SkippableFact]
    public async Task SeedSampleSearchData_Form_RendersFivePresetsAndCopyAndSubmit()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Linux),
            "Playwright browser test Linux-only");

        try
        {
            var adminCookie = await AuthHelpers.ImpersonateAsAdminAsync(Fixture);
            AttachCookieToContext(adminCookie);

            var response = await Page.GotoAsync($"{Fixture.BaseUrl}/admin/test-data/sample-search-data");
            Assert.NotNull(response);
            Assert.Equal(200, response!.Status);

            await Expect(Page.Locator("h1", new PageLocatorOptions { HasTextString = "Seed sample search data" }))
                .ToBeVisibleAsync();

            // Explanation copy calls out the demo purpose + retention window.
            var bodyText = await Page.Locator("body").InnerTextAsync();
            Assert.Contains("analytics dashboard", bodyText);
            Assert.Contains("local development", bodyText);

            // Five preset radios, all named "preset".
            var presetRadios = Page.Locator("input[name=\"preset\"][type=\"radio\"]");
            Assert.Equal(5, await presetRadios.CountAsync());
            var presetValues = await presetRadios.EvaluateAllAsync<string[]>(
                "els => els.map(e => e.value)");
            Assert.Contains("24h", presetValues);
            Assert.Contains("week", presetValues);
            Assert.Contains("month", presetValues);
            Assert.Contains("quarter", presetValues);
            Assert.Contains("year", presetValues);

            // The Last-24-hours preset is the pre-checked default.
            var defaultChecked = Page.Locator("input#preset-24h[checked], input#preset-24h:checked");
            Assert.True(await defaultChecked.CountAsync() >= 1,
                "Last 24 hours preset should be the default checked radio.");

            // Submit button present.
            var submit = Page.Locator("button[data-testid=\"seed-sample-search-data-submit\"]");
            await Expect(submit).ToBeVisibleAsync();
            Assert.Equal("Seed data", (await submit.InnerTextAsync()).Trim());

            await Page.StabiliseAsync();
            await SaveScreenshotAsync("seed-sample-search-data-form.png");
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(Fixture);
        }
    }

    // --- Full round-trip: submit the form, assert banner + link + dashboard population ---

    [SkippableFact]
    public async Task SeedSampleSearchData_SubmitLast24Hours_BannerAppearsAndDashboardHasData()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Linux),
            "Playwright browser test Linux-only");

        try
        {
            var adminCookie = await AuthHelpers.ImpersonateAsAdminAsync(Fixture);
            AttachCookieToContext(adminCookie);

            await Page.GotoAsync($"{Fixture.BaseUrl}/admin/test-data/sample-search-data");
            await Expect(Page.Locator("input#preset-24h:checked")).ToBeVisibleAsync();

            // Submit. Server redirects back to the same page with a TempData banner.
            await Page.Locator("button[data-testid=\"seed-sample-search-data-submit\"]").ClickAsync();
            await Page.WaitForURLAsync("**/admin/test-data/sample-search-data");

            // Success banner appears with the seeded numbers + a link to /admin/Search/.
            var banner = Page.Locator("div[data-testid=\"seed-sample-search-data-banner\"]");
            await Expect(banner).ToBeVisibleAsync();
            var bannerText = await banner.InnerTextAsync();
            Assert.Contains("Seeded", bannerText);
            Assert.Contains("search events", bannerText);
            Assert.Contains("feedback messages", bannerText);
            Assert.Contains("the last 24 hours", bannerText);

            // Link back to the dashboard is present.
            var dashboardLink = banner.Locator("a[href=\"/admin/Search/\"]");
            Assert.Equal(1, await dashboardLink.CountAsync());

            await Page.StabiliseAsync();
            await SaveScreenshotAsync("seed-sample-search-data-success.png");

            // Visit the dashboard on the 24-hour range and assert the volume tile has landed
            // at or above the preset default (500 events). Prior test runs may have already
            // seeded events, so >= 500 is the safe floor.
            var dash = await Page.GotoAsync($"{Fixture.BaseUrl}/admin/Search/?range=24h");
            Assert.NotNull(dash);
            Assert.Equal(200, dash!.Status);

            var volumeTile = Page.Locator("button.sa-tile[data-sa-tile=\"volume\"] .sa-tile__value");
            await Expect(volumeTile).ToBeVisibleAsync();
            var volumeText = (await volumeTile.InnerTextAsync()).Trim().Replace(",", "");
            Assert.True(int.TryParse(volumeText, out var volume),
                $"Volume tile value '{volumeText}' was not a plain integer.");
            Assert.True(volume >= 500,
                $"Expected the volume tile to be at least the 24-hour preset default (500); got {volume}.");

            await Page.StabiliseAsync();
            await SaveScreenshotAsync("admin-search-with-seeded-data.png");
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

    private Task SaveScreenshotAsync(string filename) => SaveScreenshotAsync(Page, filename);

    private static async Task SaveScreenshotAsync(IPage page, string filename)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Snapshots", "search-ux", "round8");
        Directory.CreateDirectory(dir);
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(dir, filename),
            FullPage = true,
        });
    }
}
