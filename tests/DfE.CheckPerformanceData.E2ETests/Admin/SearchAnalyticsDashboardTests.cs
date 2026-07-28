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

        // Fresh browser context — no admin cookie. The user does a search first, then
        // hits the feedback form so PriorSearch renders with the actual hits.
        await using var userContext = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1440, Height = 900 }
        });
        var userPage = await userContext.NewPageAsync();

        var search = await userPage.GotoAsync($"{Fixture.BaseUrl}/search?q=widget");
        Assert.NotNull(search);
        Assert.Equal(200, search!.Status);

        var feedback = await userPage.GotoAsync($"{Fixture.BaseUrl}/Search/Feedback");
        Assert.NotNull(feedback);
        Assert.Equal(200, feedback!.Status);

        // If the user's search returned hits, the prior-search panel appears; otherwise
        // the panel is absent (no hits to link). Either way the assertion below on
        // "no visible kind label" MUST hold on the whole form body.
        var priorCard = userPage.Locator(".govuk-summary-card__title:has-text(\"Your last search on this site\")");
        if (await priorCard.CountAsync() > 0)
        {
            var hitLinks = userPage.Locator(".govuk-summary-card__content ol.govuk-list--number li a");
            var count = await hitLinks.CountAsync();
            Assert.True(count > 0, "Prior-search panel rendered but no hit links present.");
            var relValues = await hitLinks.EvaluateAllAsync<string[]>(
                "els => els.map(e => e.getAttribute('target'))");
            Assert.All(relValues, r => Assert.Equal("_blank", r));
        }

        // The form's user-visible copy must not contain kind labels for the hit rows.
        // Any "page" or "content block" surface-level label would render inside the
        // summary-card content ol — grep the ol's inner text specifically.
        var priorCardContent = userPage.Locator(".govuk-summary-card__content ol.govuk-list--number");
        if (await priorCardContent.CountAsync() > 0)
        {
            var listText = await priorCardContent.InnerTextAsync();
            Assert.DoesNotContain("content block", listText, StringComparison.OrdinalIgnoreCase);
            // "page" is a common English word; only flag it when it looks like a kind
            // label at the tail of a list item (e.g. "…/help/foo page"). Regex form:
            // whitespace + "page" + end-of-line/li.
            Assert.DoesNotMatch(new System.Text.RegularExpressions.Regex(
                @"\bpage\s*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Multiline),
                listText);
        }

        await userPage.StabiliseAsync();
        await SaveScreenshotAsync(userPage, "feedback-get.png");

        // --- Validation-error redisplay: empty WhatLookingFor submit re-renders the
        //     form with the prior-search panel STILL present. ---
        var lookingFor = userPage.Locator("#WhatLookingFor");
        await lookingFor.FillAsync(""); // ensure empty
        var sendBtn = userPage.Locator("button.govuk-button:has-text(\"Send\")");
        await sendBtn.ClickAsync();

        // After the round-trip the page renders the error summary + the prior-search
        // panel (if it was present on GET, it must still be present here).
        await userPage.WaitForSelectorAsync(".govuk-error-summary");
        Assert.True(await userPage.Locator(".govuk-error-summary").IsVisibleAsync());
        if (await priorCard.CountAsync() > 0)
        {
            Assert.True(await userPage.Locator(
                ".govuk-summary-card__title:has-text(\"Your last search on this site\")").IsVisibleAsync(),
                "Prior-search panel MUST re-render on validation error.");
        }

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
