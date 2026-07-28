using System.Runtime.InteropServices;
using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;

namespace DfE.CheckPerformanceData.E2ETests.Admin;

// End-to-end coverage for the search-analytics dashboard + feedback-form UX. Assertions
// mirror the surface the admin actually sees: tooltips + query links + bucket selector +
// separated summary cards + widened layout on /admin/Search/, and hits-always-link +
// prior-search-preserved-on-validation-error on /Search/Feedback.
//
// Runs against a live container: Playwright drives a real Chromium browser through the
// deployment reachable at CPD_E2E_BASE_URL. Screenshots for handoff land under
// tests/DfE.CheckPerformanceData.E2ETests/Snapshots/search-ux/ so the human reviewer can
// eyeball them before merging. Linux-only for browser install parity; visual assertions
// on other OSes drift on font metrics.
[Collection("E2E")]
[Trait("Category", "W4")]
public sealed class SearchAnalyticsDashboardTests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    public override BrowserNewContextOptions ContextOptions() =>
        new() { ViewportSize = new ViewportSize { Width = 1440, Height = 900 } };

    // --- Dashboard: tiles + tooltips + chart axes + bucket selector + stacked cards ---

    [SkippableFact]
    public async Task Dashboard_RendersTilesTooltipsChartAxesBucketSelectorAndStackedCards()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Linux),
            "Playwright browser test Linux-only");

        try
        {
            var adminCookie = await AuthHelpers.ImpersonateAsAdminAsync(Fixture);
            AttachCookieToContext(adminCookie);

            var url = $"{Fixture.BaseUrl}/admin/Search/";
            var response = await Page.GotoAsync(url);
            Assert.NotNull(response);
            Assert.Equal(200, response!.Status);

            await Page.Locator(".sa-tiles").WaitForAsync(
                new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

            // 4 tiles, each with a title attribute (hover tooltip).
            var tilesWithTitle = Page.Locator(".sa-tile[title]");
            Assert.Equal(4, await tilesWithTitle.CountAsync());

            // Chart svg present. Axis-tick labels rendered as <text>.
            await Expect(Page.Locator(".sa-chart")).ToBeVisibleAsync();
            var axisTickCount = await Page.Locator(".sa-chart .sa-chart__axis-tick").CountAsync();
            Assert.True(axisTickCount >= 3, $"Expected at least 3 axis tick labels, got {axisTickCount}");
            var axisTitleCount = await Page.Locator(".sa-chart .sa-chart__axis-title").CountAsync();
            Assert.True(axisTitleCount >= 3, $"Expected 3 axis title labels, got {axisTitleCount}");

            // Bucket selector: 5 sizes.
            var bucketRadios = Page.Locator("input[name=\"bucket\"]");
            Assert.Equal(5, await bucketRadios.CountAsync());
            var values = await bucketRadios.EvaluateAllAsync<string[]>(
                "els => els.map(e => e.value)");
            Assert.Contains("15m", values);
            Assert.Contains("1h",  values);
            Assert.Contains("1d",  values);
            Assert.Contains("1w",  values);
            Assert.Contains("1mo", values);

            // Two summary cards for the two query tables, each on its own row.
            var summaryCards = Page.Locator(".sa-summary-card");
            Assert.Equal(2, await summaryCards.CountAsync());

            // Every query link in either table opens in a new tab.
            var queryLinks = Page.Locator(".sa-summary-card table.govuk-table td a[target=\"_blank\"]");
            var queryLinkCount = await queryLinks.CountAsync();
            // When the window has data there's at least one link; when empty the assertion
            // is vacuously true (no links = no non-blank links either).
            if (queryLinkCount > 0)
            {
                var relValues = await queryLinks.EvaluateAllAsync<string[]>(
                    "els => els.map(e => e.getAttribute('rel'))");
                Assert.All(relValues, r => Assert.NotNull(r));
                Assert.All(relValues, r => Assert.Contains("noopener", r));
            }

            // AdminWide flag → the wide layout wrapper is used.
            await Expect(Page.Locator(".admin-layout--wide")).ToBeVisibleAsync();

            await Page.StabiliseAsync();
            await SaveScreenshotAsync("admin-search.png");
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(Fixture);
        }
    }

    // --- Feedback form: every hit renders as a new-tab link; no kind labels visible ---

    [SkippableFact]
    public async Task FeedbackForm_AfterUserSearch_HitsRenderAsNewTabLinksAndNoKindLabels()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Linux),
            "Playwright browser test Linux-only");

        // Fresh browser context. In Development the app is configured to emit a
        // SecurePolicy=SameAsRequest session cookie so plain-http Chromium (hitting
        // host.docker.internal) actually persists it across requests. We ALSO impersonate
        // as editor so the auto-fill-email path (F3) is exercised — the impersonation
        // cookie set here does NOT rotate the session id (see CONTEXT D-04) so the search
        // fired below still ends up linked to the same session_id the feedback form reads.
        await using var userContext = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1440, Height = 900 }
        });
        var userPage = await userContext.NewPageAsync();

        var editorCookie = await AuthHelpers.ImpersonateAsEditorAsync(Fixture);
        if (!string.IsNullOrEmpty(editorCookie))
        {
            var eq = editorCookie.IndexOf('=');
            if (eq > 0)
            {
                await userContext.AddCookiesAsync([new Cookie
                {
                    Name = editorCookie[..eq],
                    Value = editorCookie[(eq + 1)..],
                    Url = Fixture.BaseUrl,
                }]);
            }
        }

        var search = await userPage.GotoAsync($"{Fixture.BaseUrl}/search?q=widget");
        Assert.NotNull(search);
        Assert.Equal(200, search!.Status);

        var feedback = await userPage.GotoAsync($"{Fixture.BaseUrl}/Search/Feedback");
        Assert.NotNull(feedback);
        Assert.Equal(200, feedback!.Status);

        // The prior-search panel MUST render — the SearchEventWriter drain is async so a
        // short wait covers the between-request race. If the panel still isn't there we
        // want a hard failure, not a silent skip.
        var priorCard = userPage.Locator(".govuk-summary-card__title:has-text(\"Your last search on this site\")");
        await priorCard.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        Assert.Equal(1, await priorCard.CountAsync());

        var hitLinks = userPage.Locator(".govuk-summary-card__content ol.govuk-list--number li a");
        var linkCount = await hitLinks.CountAsync();
        Assert.True(linkCount > 0, "Prior-search panel rendered but no hit links present.");
        var relValues = await hitLinks.EvaluateAllAsync<string[]>(
            "els => els.map(e => e.getAttribute('target'))");
        Assert.All(relValues, r => Assert.Equal("_blank", r));

        // The form's user-visible copy must not contain kind labels for the hit rows.
        // Round-1 shipped labels like "home - content block" and "…/help/foo page"; the
        // fix strips both. Assert on "content block" verbatim (the phrase never appears
        // in a legitimate URL). Do NOT regex-match the word "page" alone — URL segments
        // like "…creating-a-page" contain it and produce false positives; if a future
        // change reintroduces a " - page" suffix, a follow-on assertion on the specific
        // suffix pattern will catch it.
        var priorCardContent = userPage.Locator(".govuk-summary-card__content ol.govuk-list--number");
        var listText = await priorCardContent.InnerTextAsync();
        Assert.DoesNotContain("content block", listText, StringComparison.OrdinalIgnoreCase);
        // Kind-label as trailing suffix after a separator: " - page" / " – page" / "\tpage" at line end.
        Assert.DoesNotMatch(new System.Text.RegularExpressions.Regex(
            @"[ \t][-–][ \t]page\s*$|[ \t][-–][ \t]content[ \t]block\s*$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Multiline),
            listText);

        // F3: signed-in editor sees their email pre-filled.
        var emailValue = await userPage.Locator("#Email").InputValueAsync();
        Assert.False(string.IsNullOrWhiteSpace(emailValue),
            $"Email should be auto-filled from the signed-in user's claim. Actual value: '{emailValue}'.");
        Assert.Contains("@", emailValue);

        await userPage.StabiliseAsync();
        await SaveScreenshotAsync(userPage, "feedback-get.png");

        // --- Validation-error redisplay: empty WhatLookingFor submit re-renders the
        //     form with the prior-search panel STILL present AND the email still filled. ---
        await userPage.Locator("#WhatLookingFor").FillAsync(""); // ensure empty
        await userPage.Locator("button.govuk-button:has-text(\"Send\")").ClickAsync();

        await userPage.WaitForSelectorAsync(".govuk-error-summary");
        Assert.True(await userPage.Locator(".govuk-error-summary").IsVisibleAsync());
        Assert.Equal(1, await userPage.Locator(
            ".govuk-summary-card__title:has-text(\"Your last search on this site\")").CountAsync());

        var emailAfterError = await userPage.Locator("#Email").InputValueAsync();
        Assert.Equal(emailValue, emailAfterError);

        await userPage.StabiliseAsync();
        await SaveScreenshotAsync(userPage, "feedback-post-error.png");
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

    // Handoff screenshots — path-safe under the test project so the human reviewer can
    // browse them from git. Not versus a baseline; not a visual-regression assertion.
    private Task SaveScreenshotAsync(string filename) => SaveScreenshotAsync(Page, filename);

    private static async Task SaveScreenshotAsync(IPage page, string filename)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Snapshots", "search-ux");
        Directory.CreateDirectory(dir);
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(dir, filename),
            FullPage = true,
        });
    }
}
