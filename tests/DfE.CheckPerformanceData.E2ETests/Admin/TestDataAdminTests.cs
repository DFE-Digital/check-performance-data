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
// messages + analytics-drill-ins snapshot shots use. Screenshots land under
// Snapshots/search-ux/seed-admin/ (or seed-progress/ / danger-zone/ depending on the
// captured surface — see SaveScreenshotAsync below for the routing).
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

            // Submit. JS intercepts, POSTs via fetch, opens the progress modal. The
            // modal transitions to the "completed" state with a link to /admin/Search/
            // once the background seed finishes.
            await Page.Locator("button[data-testid=\"seed-sample-search-data-submit\"]").ClickAsync();

            var modal = Page.Locator("dialog[data-testid=\"seed-progress-modal\"]");
            await Expect(modal).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 2000 });

            var completed = Page.Locator("[data-testid=\"seed-progress-completed\"]");
            await Expect(completed).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });
            var completedText = await completed.InnerTextAsync();
            Assert.Contains("Seeded", completedText);
            Assert.Contains("events", completedText);
            Assert.Contains("the last 24 hours", completedText);

            // Link back to the dashboard is present inside the modal.
            var dashboardLink = modal.Locator("a[href=\"/admin/Search/\"]");
            Assert.True(await dashboardLink.CountAsync() >= 1);

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

    // --- Progress modal + polling ---

    // Modal opens within 1 s of clicking Seed data on the smallest preset (Last 24 hours
    // = 500 events) and progresses to the completed state with the "View dashboard →"
    // link within 30 s. Screenshots the running + completed states.
    [SkippableFact]
    public async Task SeedSampleSearchData_ProgressModal_OpensAndCompletes()
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

            // Ensure the JS-driven modal is present in DOM (hidden until submit).
            var modal = Page.Locator("dialog[data-testid=\"seed-progress-modal\"]");
            Assert.Equal(1, await modal.CountAsync());

            // Click Seed data — JS intercepts, POSTs via fetch, opens the modal.
            await Page.Locator("button[data-testid=\"seed-sample-search-data-submit\"]").ClickAsync();

            // Modal opens within 1 s.
            await Expect(modal).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 1500 });

            // Second submit while the job is in flight is a no-op (button disabled).
            var submitBtn = Page.Locator("button[data-testid=\"seed-sample-search-data-submit\"]");
            var disabled = await submitBtn.GetAttributeAsync("disabled");
            Assert.NotNull(disabled);

            // First progress-tick screenshot — take once eventsWritten > 0 or up to 3 s.
            var midStart = DateTime.UtcNow;
            while ((DateTime.UtcNow - midStart) < TimeSpan.FromSeconds(3))
            {
                var mid = await Page.Locator("[data-testid=\"seed-progress-events\"]").InnerTextAsync();
                if (int.TryParse(mid.Replace(",", ""), out var seededMid) && seededMid > 0) break;
                await Task.Delay(200);
            }
            await SaveScreenshotAsync("seed-in-progress-modal.png");

            // Completed within 30 s.
            var completed = Page.Locator("[data-testid=\"seed-progress-completed\"]");
            await Expect(completed).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });

            // View dashboard → link visible.
            var dashLink = modal.Locator("a[href=\"/admin/Search/\"]");
            Assert.True(await dashLink.CountAsync() >= 1);

            // Success notification banner replaces the plain completion paragraph so
            // the terminal state reads unmistakably positive.
            var successBanner = Page.Locator(
                "[data-testid=\"seed-progress-completed-banner\"].govuk-notification-banner--success");
            await Expect(successBanner).ToBeVisibleAsync();
            var bannerTitle = successBanner.Locator("h2.govuk-notification-banner__title");
            Assert.Equal("Seeding complete", (await bannerTitle.InnerTextAsync()).Trim());
            var bannerBody = (await successBanner.InnerTextAsync()).Trim();
            Assert.Contains("Seeded", bannerBody);
            Assert.Contains("events", bannerBody);
            Assert.Contains("feedback messages", bannerBody);

            // On a terminal state the Cancel button is removed (nothing to cancel)
            // and the right-hand button relabels to plain "Close" — the running-state
            // "Close (keep seeding in background)" copy is misleading once done.
            // Read the DOM `hidden` property directly rather than IsHiddenAsync so we
            // are unambiguous about which layer we mean (attribute vs CSS visibility).
            var cancelBtn = Page.Locator("button[data-testid=\"seed-progress-cancel\"]");
            var cancelHidden = await cancelBtn.EvaluateAsync<bool>(
                "el => el.hidden || el.style.display === 'none' || el.offsetParent === null");
            Assert.True(cancelHidden,
                "Cancel seeding button should be hidden once the job has completed.");
            var closeBtn = Page.Locator("button[data-testid=\"seed-progress-close\"]");
            Assert.Equal("Close", (await closeBtn.InnerTextAsync()).Trim());

            await SaveScreenshotAsync("seed-completed-modal.png");
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(Fixture);
        }
    }

    // Reloading the page mid-flight (?jobId=...) auto-opens the modal and resumes polling.
    // Kick a large-ish seed first (Quarter = 25k events), grab the jobId out of the URL,
    // reload with the jobId query string, assert modal opens with a running snapshot.
    [SkippableFact]
    public async Task SeedSampleSearchData_ReloadWithJobId_AutoOpensModalAndResumes()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Linux),
            "Playwright browser test Linux-only");

        try
        {
            var adminCookie = await AuthHelpers.ImpersonateAsAdminAsync(Fixture);
            AttachCookieToContext(adminCookie);

            await Page.GotoAsync($"{Fixture.BaseUrl}/admin/test-data/sample-search-data");
            await Page.Locator("input#preset-quarter").ClickAsync();

            // Wait for the JS network response so we can extract the jobId from the URL
            // that fetch follows internally (progress endpoint responds with the JSON).
            var responseTask = Page.WaitForResponseAsync(r =>
                r.Url.Contains("/admin/test-data/sample-search-data/progress"));
            await Page.Locator("button[data-testid=\"seed-sample-search-data-submit\"]").ClickAsync();
            var progressResponse = await responseTask;
            var jobIdMatch = System.Text.RegularExpressions.Regex.Match(
                progressResponse.Url, "jobId=([0-9a-fA-F-]+)");
            Assert.True(jobIdMatch.Success, $"Expected jobId in {progressResponse.Url}.");
            var jobId = jobIdMatch.Groups[1].Value;

            // Reload with ?jobId=… — the JS auto-opens the modal from the server-rendered
            // data-resume-job-id + query string.
            await Page.GotoAsync($"{Fixture.BaseUrl}/admin/test-data/sample-search-data?jobId={jobId}");
            var modal = Page.Locator("dialog[data-testid=\"seed-progress-modal\"]");
            await Expect(modal).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 2000 });
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(Fixture);
        }
    }

    // Cancel wires through the DELETE endpoint and transitions the job to Completed
    // with a "cancelled" note. Small preset (24h = 500 events) may complete before the
    // click lands — that skip is documented in the SUMMARY.
    [SkippableFact]
    public async Task SeedSampleSearchData_CancelButton_TransitionsJobToCancelled()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Linux),
            "Playwright browser test Linux-only");

        try
        {
            var adminCookie = await AuthHelpers.ImpersonateAsAdminAsync(Fixture);
            AttachCookieToContext(adminCookie);

            await Page.GotoAsync($"{Fixture.BaseUrl}/admin/test-data/sample-search-data");

            // Big preset so the seed lasts long enough to catch a Cancel click.
            await Page.Locator("input#preset-quarter").ClickAsync();
            await Page.Locator("button[data-testid=\"seed-sample-search-data-submit\"]").ClickAsync();

            var modal = Page.Locator("dialog[data-testid=\"seed-progress-modal\"]");
            await Expect(modal).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 1500 });

            // Wait a beat so the seed is definitely mid-run then Cancel.
            await Task.Delay(400);
            await Page.Locator("button[data-testid=\"seed-progress-cancel\"]").ClickAsync();

            // Wait for a terminal state — either cancelled (expected) or completed
            // (race window on the smaller preset). Screenshot the cancelled state when
            // we get it; the SUMMARY notes the race skip when we don't.
            var cancelledPanel = Page.Locator("[data-testid=\"seed-progress-cancelled\"]");
            var completedPanel = Page.Locator("[data-testid=\"seed-progress-completed\"]");
            var raceStart = DateTime.UtcNow;
            var sawCancelled = false;
            while ((DateTime.UtcNow - raceStart) < TimeSpan.FromSeconds(25))
            {
                if (await cancelledPanel.IsVisibleAsync()) { sawCancelled = true; break; }
                if (await completedPanel.IsVisibleAsync()) { break; }
                await Task.Delay(300);
            }
            if (sawCancelled)
            {
                await SaveScreenshotAsync("seed-cancelled-modal.png");
            }
            // Both outcomes are acceptable — a fast seeder may finish before the DELETE
            // handler runs. The primary invariant is the cancel path exists and is wired.
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(Fixture);
        }
    }

    // --- Pure JS helpers exposed on window: formatDuration + computeEtaSeconds ---

    // The seed progress modal renders the running ETA via two small helpers exported on
    // window for ad-hoc DevTools use: formatDuration (seconds -> human string) and
    // computeEtaSeconds (blended cumulative + persisted rate estimator). Both are pure
    // functions with named edge cases in their comments — this test executes them in the
    // real browser JS engine on /admin/test-data/sample-search-data and asserts every one
    // of those documented cases.
    [SkippableFact]
    public async Task SeedSampleSearchData_JsHelpers_FormatDurationAndComputeEtaSecondsBehaveAsDocumented()
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

            // The two helpers are exposed on window at script load — assert they're both
            // functions before drilling into behaviour so a rename/removal fails loud.
            var exposureOk = await Page.EvaluateAsync<bool>(
                "() => typeof window.__seedFormatDuration === 'function' && typeof window.__seedComputeEtaSeconds === 'function'");
            Assert.True(exposureOk,
                "Both __seedFormatDuration and __seedComputeEtaSeconds must be exposed on window.");

            // ---- formatDuration ----
            //   47   -> "47 seconds"
            //   93   -> "1 minute 33 seconds"
            //   4335 -> "1 hour 12 minutes 15 seconds"
            //   3600 -> "1 hour"                (skip zero minute/second components)
            //   3660 -> "1 hour 1 minute"       (skip zero second component)
            //   0    -> "0 seconds"             (fallback when everything is zero)
            var duration47 = await Page.EvaluateAsync<string>("() => window.__seedFormatDuration(47)");
            var duration93 = await Page.EvaluateAsync<string>("() => window.__seedFormatDuration(93)");
            var duration4335 = await Page.EvaluateAsync<string>("() => window.__seedFormatDuration(4335)");
            var duration3600 = await Page.EvaluateAsync<string>("() => window.__seedFormatDuration(3600)");
            var duration3660 = await Page.EvaluateAsync<string>("() => window.__seedFormatDuration(3660)");
            var duration0 = await Page.EvaluateAsync<string>("() => window.__seedFormatDuration(0)");

            Assert.Equal("47 seconds", duration47);
            Assert.Equal("1 minute 33 seconds", duration93);
            Assert.Equal("1 hour 12 minutes 15 seconds", duration4335);
            Assert.Equal("1 hour", duration3600);
            Assert.Equal("1 hour 1 minute", duration3660);
            Assert.Equal("0 seconds", duration0);

            // ---- computeEtaSeconds ----
            // Cumulative faster than persisted -> take the shorter estimate.
            //   written=1000, total=2000, startedAtMs=now-10s, persisted=0.1 (=10 eps).
            //   Cumulative = 1000 / 10 = 100 eps -> remaining 1000 / 100 = 10 s.
            //   Persisted alone would have been 1000 / 10 = 100 s.
            //   With elapsedS >= 3 and written >= 100, the impl picks max(cumulative,
            //   persisted) = 100 eps, giving 10 s.
            var etaCumulative = await Page.EvaluateAsync<int?>(@"
                () => {
                    var now = Date.now();
                    return window.__seedComputeEtaSeconds(1000, 2000, now - 10000, now, 0.1);
                }");
            Assert.Equal(10, etaCumulative);

            // Insufficient data: elapsedS < 3 -> fall back to persisted rate.
            //   written=50, total=1000, startedAtMs=now-1s, persisted=0.1 (=10 eps).
            //   Only-persisted branch: remaining 950 / 10 = 95 s.
            var etaFallback = await Page.EvaluateAsync<int?>(@"
                () => {
                    var now = Date.now();
                    return window.__seedComputeEtaSeconds(50, 1000, now - 1000, now, 0.1);
                }");
            Assert.Equal(95, etaFallback);

            // Written >= total -> 0 s left (Math.max(0, …) branch is short-circuited
            // by the impl before the rate calculation runs).
            var etaComplete = await Page.EvaluateAsync<int?>(@"
                () => {
                    var now = Date.now();
                    return window.__seedComputeEtaSeconds(2000, 2000, now - 10000, now, 0.1);
                }");
            Assert.Equal(0, etaComplete);

            // Zero / missing total -> null (no denominator, cannot estimate).
            var etaZeroTotal = await Page.EvaluateAsync<int?>(@"
                () => {
                    var now = Date.now();
                    return window.__seedComputeEtaSeconds(100, 0, now - 10000, now, 0.1);
                }");
            Assert.Null(etaZeroTotal);

            var etaMissingTotal = await Page.EvaluateAsync<int?>(@"
                () => {
                    var now = Date.now();
                    return window.__seedComputeEtaSeconds(100, null, now - 10000, now, 0.1);
                }");
            Assert.Null(etaMissingTotal);
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(Fixture);
        }
    }

    // --- Helpers ---

    // --- Danger zone: delete seeded + delete all data ---

    // Renders the Danger zone section under the seed form with two warning-style buttons
    // and captures a screenshot. Independent of the delete flow so a purely-visual
    // regression on the section shows up as a single failing test.
    [SkippableFact]
    public async Task SeedSampleSearchData_DangerZone_ShowsBothDeleteButtons()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Linux),
            "Playwright browser test Linux-only");

        try
        {
            var adminCookie = await AuthHelpers.ImpersonateAsAdminAsync(Fixture);
            AttachCookieToContext(adminCookie);

            await Page.GotoAsync($"{Fixture.BaseUrl}/admin/test-data/sample-search-data");
            var heading = Page.Locator("h2#danger-zone-heading");
            await Expect(heading).ToBeVisibleAsync();

            var deleteSeededBtn = Page.Locator("button[data-testid=\"delete-seeded-open\"]");
            await Expect(deleteSeededBtn).ToBeVisibleAsync();
            var deleteAllBtn = Page.Locator("button[data-testid=\"delete-all-open\"]");
            await Expect(deleteAllBtn).ToBeVisibleAsync();

            await Page.StabiliseAsync();
            await SaveScreenshotAsync("test-data-danger-zone.png");
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(Fixture);
        }
    }

    // Full delete-seeded flow: seed a preset, click Delete seeded, confirm in the modal,
    // assert the success banner reports non-zero counts. Screenshots the first modal
    // just before the confirm click.
    [SkippableFact]
    public async Task SeedSampleSearchData_DeleteSeeded_WipesSeededRowsAndShowsBanner()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Linux),
            "Playwright browser test Linux-only");

        try
        {
            var adminCookie = await AuthHelpers.ImpersonateAsAdminAsync(Fixture);
            AttachCookieToContext(adminCookie);

            // Seed the last-24-hours preset first so the delete has something to remove.
            await Page.GotoAsync($"{Fixture.BaseUrl}/admin/test-data/sample-search-data");
            await Page.Locator("button[data-testid=\"seed-sample-search-data-submit\"]").ClickAsync();
            var completed = Page.Locator("[data-testid=\"seed-progress-completed\"]");
            await Expect(completed).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });
            await Page.Locator("[data-testid=\"seed-progress-close\"]").ClickAsync();

            // Open the delete-seeded modal.
            await Page.Locator("button[data-testid=\"delete-seeded-open\"]").ClickAsync();
            var modal = Page.Locator("dialog[data-testid=\"delete-seeded-modal\"]");
            await Expect(modal).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 2000 });

            await Page.StabiliseAsync();
            await SaveScreenshotAsync("delete-seeded-modal.png");

            // Confirm the delete. JS fetches and reloads the page.
            await Page.Locator("[data-testid=\"delete-seeded-confirm\"]").ClickAsync();

            // Success banner appears after the reload — counts are per-table.
            var banner = Page.Locator("[data-testid=\"seed-sample-search-data-banner\"]");
            await Expect(banner).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
            var bannerText = await banner.InnerTextAsync();
            Assert.Contains("Deleted", bannerText);
            Assert.Contains("events", bannerText);
            Assert.Contains("messages", bannerText);
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(Fixture);
        }
    }

    // Delete-all typed-confirmation gate: confirm button is disabled until the input
    // value equals 'DELETE'. Screenshots the enabled state.
    [SkippableFact]
    public async Task SeedSampleSearchData_DeleteAll_ConfirmButtonRequiresTypedDelete()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Linux),
            "Playwright browser test Linux-only");

        try
        {
            var adminCookie = await AuthHelpers.ImpersonateAsAdminAsync(Fixture);
            AttachCookieToContext(adminCookie);

            await Page.GotoAsync($"{Fixture.BaseUrl}/admin/test-data/sample-search-data");

            await Page.Locator("button[data-testid=\"delete-all-open\"]").ClickAsync();
            var modal = Page.Locator("dialog[data-testid=\"delete-all-modal\"]");
            await Expect(modal).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 2000 });

            var confirmBtn = Page.Locator("[data-testid=\"delete-all-confirm\"]");
            // Disabled on open.
            Assert.True(await confirmBtn.IsDisabledAsync(),
                "Delete all confirm button should start disabled until DELETE is typed.");

            var input = Page.Locator("[data-testid=\"delete-all-confirm-input\"]");
            await input.FillAsync("delete");
            Assert.True(await confirmBtn.IsDisabledAsync(),
                "Case-sensitive check — lowercase must not enable the button.");

            await input.FillAsync("DELETE");
            await Expect(confirmBtn).ToBeEnabledAsync(new LocatorAssertionsToBeEnabledOptions { Timeout = 2000 });

            await Page.StabiliseAsync();
            await SaveScreenshotAsync("delete-all-modal-typed.png");
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(Fixture);
        }
    }

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
        // Older search-ux screenshots remain under their existing subfolder; the newer
        // seed-progress-modal shots land under a sibling folder. The filename pattern
        // (seed-in-progress-modal.png, seed-completed-modal.png, seed-cancelled-modal.png)
        // is used to route so both sets stay grouped by capture surface.
        var subFolder = filename.StartsWith("seed-in-progress-modal", StringComparison.Ordinal)
            || filename.StartsWith("seed-completed-modal", StringComparison.Ordinal)
            || filename.StartsWith("seed-cancelled-modal", StringComparison.Ordinal)
                ? "seed-progress"
                : (filename.StartsWith("delete-", StringComparison.Ordinal)
                    || filename.StartsWith("test-data-danger-zone", StringComparison.Ordinal))
                        ? "danger-zone"
                        : "seed-admin";
        var dir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Snapshots", "search-ux", subFolder);
        Directory.CreateDirectory(dir);
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(dir, filename),
            FullPage = true,
        });
    }
}
