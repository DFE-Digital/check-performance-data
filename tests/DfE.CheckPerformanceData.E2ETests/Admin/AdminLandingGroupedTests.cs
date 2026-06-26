using System.Net;
using System.Runtime.InteropServices;
using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;

namespace DfE.CheckPerformanceData.E2ETests.Admin;

// Verifies the /admin landing renders both the grouped tile sections (Plan 02)
// AND the new tree-nav left rail inside the two-column admin layout shell.
// The computed-CSS pin test mirrors UI-SPEC § Snapshot Fixtures "Computed-CSS
// spot pins" (rail flex-basis = 280px; group separator border = rgb(177, 180,
// 182); disabled rail row tabindex absent). The UI-SPEC is the source of truth
// for those three values.
[Collection("E2E")]
[Trait("Category", "W4")]
public sealed class AdminLandingGroupedTests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    public override BrowserNewContextOptions ContextOptions() =>
        new() { ViewportSize = new ViewportSize { Width = 1280, Height = 720 } };

    // --- AdminLanding_AsAdmin_Renders_Two_Group_Section_Headers ---

    [Fact]
    public async Task AdminLanding_AsAdmin_Renders_Two_Group_Section_Headers()
    {
        try
        {
            await AuthHelpers.ImpersonateAsAdminAsync(Fixture);

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{Fixture.BaseUrl}/admin");

            var response = await TestHttpClients.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("<h2 ", body);
            Assert.Contains("CMS administration", body);
            Assert.Contains("System administration", body);
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(Fixture);
        }
    }

    // --- AdminLanding_AsAdmin_Renders_AdminLayout_Two_Column_Shell ---

    [Fact]
    public async Task AdminLanding_AsAdmin_Renders_AdminLayout_Two_Column_Shell()
    {
        try
        {
            await AuthHelpers.ImpersonateAsAdminAsync(Fixture);

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{Fixture.BaseUrl}/admin");

            var response = await TestHttpClients.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("class=\"admin-layout\"", body);
            Assert.Contains("class=\"admin-rail\"", body);
            Assert.Contains("class=\"admin-main\"", body);
            Assert.Contains("id=\"main-content\"", body);
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(Fixture);
        }
    }

    // --- AdminLanding_AsAdmin_Renders_AdminNavTree_Partial_With_tv_root ---

    [Fact]
    public async Task AdminLanding_AsAdmin_Renders_AdminNavTree_Partial_With_tv_root()
    {
        try
        {
            await AuthHelpers.ImpersonateAsAdminAsync(Fixture);

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{Fixture.BaseUrl}/admin");

            var response = await TestHttpClients.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("aria-label=\"Administration navigation\"", body);
            Assert.Contains("tv tv-root", body);
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(Fixture);
        }
    }

    // --- AdminLanding_AsAdmin_Rail_HasNoDisabledPlaceholderTiles ---

    [Fact]
    public async Task AdminLanding_AsAdmin_Rail_HasNoDisabledPlaceholderTiles()
    {
        // Every admin nav entry is now an actionable link — the last "Coming soon"
        // placeholder tile (version retention) became a real setting. Guard against a
        // disabled/placeholder tile being reintroduced.
        try
        {
            await AuthHelpers.ImpersonateAsAdminAsync(Fixture);

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{Fixture.BaseUrl}/admin");

            var response = await TestHttpClients.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("aria-disabled=\"true\"", body);
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(Fixture);
        }
    }

    // --- AdminLanding_Rail_ComputedCss_PinsMatchUiSpec ---

    // Source of truth: UI-SPEC § Snapshot Fixtures § Computed-CSS spot pins.
    // The runtime assertions below mirror those pins exactly:
    //   (a) .admin-rail flex-basis = 280px
    //   (b) second .admin-nav-group border-top-color = rgb(177, 180, 182) (#b1b4b6)
    // (The former disabled-row tabindex pin was dropped — no admin tile is disabled
    // anymore, see AdminLanding_AsAdmin_Rail_HasNoDisabledPlaceholderTiles.)
    // Linux-only (Playwright VR convention; mirrors AdminLandingVisualTests).
    [SkippableFact]
    public async Task AdminLanding_Rail_ComputedCss_PinsMatchUiSpec()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Linux),
            "Playwright browser test Linux-only");

        try
        {
            var adminCookie = await AuthHelpers.ImpersonateAsAdminAsync(Fixture);
            if (!string.IsNullOrEmpty(adminCookie))
            {
                var equalsIndex = adminCookie.IndexOf('=');
                if (equalsIndex > 0)
                {
                    await Context.AddCookiesAsync([new Microsoft.Playwright.Cookie
                    {
                        Name = adminCookie[..equalsIndex],
                        Value = adminCookie[(equalsIndex + 1)..],
                        Url = Fixture.BaseUrl
                    }]);
                }
            }

            await Page.GotoAsync($"{Fixture.BaseUrl}/admin");
            // Wait for the rail element, not NetworkIdle: deployed environments emit
            // background telemetry beacons that keep NetworkIdle from settling within
            // the 30s timeout (intermittent CI failure). The computed-CSS pins below
            // only need the rail rendered and styled, which the load event guarantees.
            await Page.Locator(".admin-rail").WaitForAsync(
                new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

            // (a) Rail width pinned to 280px
            var railFlexBasis = await Page.EvaluateAsync<string>(
                "() => getComputedStyle(document.querySelector('.admin-rail')).flexBasis");
            Assert.Equal("280px", railFlexBasis);

            // (b) Group separator border colour pins #b1b4b6 — the only new chrome line
            //     introduced by this phase. Computed colour resolves to rgb(177, 180, 182).
            var sepColour = await Page.EvaluateAsync<string>(
                "() => { var groups = document.querySelectorAll('.admin-nav-group');" +
                " return groups.length >= 2 ? getComputedStyle(groups[1]).borderTopColor : null; }");
            Assert.Equal("rgb(177, 180, 182)", sepColour);
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(Fixture);
        }
    }
}
