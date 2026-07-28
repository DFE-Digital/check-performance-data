using System.Runtime.InteropServices;
using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;

namespace DfE.CheckPerformanceData.E2ETests.Admin;

// End-to-end coverage for the consolidated Messages admin nav surface:
//   * A consolidated Messages top-bar badge stands in for the retired DLQ + inbox badges.
//   * A root-level Messages group on the /admin landing owns the Search-feedback inbox +
//     Dead-letter-queue tiles, addressable at /admin/Messages.
//   * The admin message-detail page renders the user's typed content AND the pre-submission
//     search snapshot (query + hit list) so an admin can see what the user was looking at.
//
// Runs against a live container: Playwright drives a real Chromium browser through the
// deployment reachable at CPD_E2E_BASE_URL. Screenshots for handoff land under
// tests/DfE.CheckPerformanceData.E2ETests/Snapshots/search-ux/. Linux-only for browser
// install parity; visual assertions on other OSes drift on font metrics.
[Collection("E2E")]
[Trait("Category", "W4")]
public sealed class MessagesGroupAndDetailTests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    public override BrowserNewContextOptions ContextOptions() =>
        new() { ViewportSize = new ViewportSize { Width = 1440, Height = 900 } };

    // --- Consolidated top-bar badge + Messages group landing ---

    [SkippableFact]
    public async Task AdminChrome_MessagesBadgeReplacesPair_AndGroupLandingListsBothTiles()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Linux),
            "Playwright browser test Linux-only");

        try
        {
            var adminCookie = await AuthHelpers.ImpersonateAsAdminAsync(Fixture);
            AttachCookieToContext(adminCookie);

            // Post one feedback so the badge count is at least 1 for the numeric-sum assertion.
            await PostFeedbackAsync("widget", "widget was not returning results i expected", "u@example.com");

            var adminLanding = await Page.GotoAsync($"{Fixture.BaseUrl}/admin/");
            Assert.NotNull(adminLanding);
            Assert.Equal(200, adminLanding!.Status);

            // The Messages group is a top-level section on the /admin landing.
            var messagesGroupHeading = Page.Locator("h2#group-messages-group");
            await Expect(messagesGroupHeading).ToBeVisibleAsync();
            Assert.Equal("Messages", (await messagesGroupHeading.InnerTextAsync()).Trim());

            // The top-bar badge is a single Messages link pointing at the group landing.
            var messagesBadge = Page.Locator(".govuk-service-navigation__link[href=\"/admin/Messages\"]");
            await Expect(messagesBadge).ToBeVisibleAsync();
            // The old DLQ badge is gone. The DLQ URL still appears elsewhere (sidebar tile,
            // Messages group children); the removed badge is the standalone service-nav item
            // linking to /admin/queues/dlq — assert on that specific selector.
            var standaloneDlqBadge = Page.Locator(
                ".govuk-service-navigation__item .govuk-service-navigation__link[href=\"/admin/queues/dlq\"]");
            Assert.Equal(0, await standaloneDlqBadge.CountAsync());

            // Badge integer = SUM of unread search-feedback + waiting-DLQ counts. We posted
            // one feedback above, so the count MUST be >= 1.
            var badgeCountText = await messagesBadge.Locator("strong.govuk-tag").InnerTextAsync();
            Assert.True(int.TryParse(badgeCountText.Trim(), out var badgeCount),
                $"Badge count '{badgeCountText}' was not a plain integer.");
            Assert.True(badgeCount >= 1,
                $"Expected the badge count to include at least the freshly-posted feedback, but got {badgeCount}.");

            await Page.StabiliseAsync();
            await SaveScreenshotAsync("admin-nav-messages-group.png");

            // Click through to the Messages group landing.
            await messagesBadge.ClickAsync();
            await Page.WaitForURLAsync("**/admin/Messages");
            await Expect(Page.Locator("h1", new PageLocatorOptions { HasTextString = "Messages" })).ToBeVisibleAsync();

            // Both child tiles present on the group landing.
            var searchFeedbackTile = Page.Locator("a.govuk-link[href=\"/admin/Messages/Inbox\"]");
            await Expect(searchFeedbackTile).ToBeVisibleAsync();
            Assert.Equal("Search feedback", (await searchFeedbackTile.InnerTextAsync()).Trim());

            var dlqTile = Page.Locator("a.govuk-link[href=\"/admin/queues/dlq\"]");
            await Expect(dlqTile).ToBeVisibleAsync();

            await Page.StabiliseAsync();
            await SaveScreenshotAsync("admin-messages-group-landing.png");
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(Fixture);
        }
    }

    // --- Admin message detail: shows the user's typed question AND the prior-search hits ---

    [SkippableFact]
    public async Task MessageDetail_ShowsUserQueryAndPriorSearchHits()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Linux),
            "Playwright browser test Linux-only");

        try
        {
            var whatLookingFor = $"e2e-{Guid.NewGuid():N} — cannot find the widget guide";

            // Post a feedback message. The message-service pins the row to the seed
            // HttpClient's session; the admin then reviews from a fresh admin session.
            await PostFeedbackAsync("widget", whatLookingFor, email: null);

            var adminCookie = await AuthHelpers.ImpersonateAsAdminAsync(Fixture);
            AttachCookieToContext(adminCookie);

            var inbox = await Page.GotoAsync($"{Fixture.BaseUrl}/admin/Messages/Inbox");
            Assert.NotNull(inbox);
            Assert.Equal(200, inbox!.Status);

            // The inbox heading rendered under the new label.
            await Expect(Page.Locator("h1", new PageLocatorOptions { HasTextString = "Search feedback" }))
                .ToBeVisibleAsync();

            // Follow the freshly-posted preview link into the detail view. Server orders
            // newest-first by default so the row we just wrote is on top.
            var firstRow = Page.Locator("tbody.govuk-table__body tr.govuk-table__row").First;
            var detailLink = firstRow.Locator("a.govuk-link[href^=\"/admin/Messages/Inbox/\"]");
            await detailLink.ClickAsync();
            await Page.WaitForURLAsync("**/admin/Messages/Inbox/*");

            // The user-typed content leads on the detail page.
            var body = await Page.Locator("body").InnerTextAsync();
            Assert.Contains(whatLookingFor, body);

            // The prior-search summary card is present with a numbered hit list; every hit
            // renders as a target=_blank link so an admin can inspect without losing their
            // place. Wait for the SearchEventWriter drain — the sink is fire-and-forget.
            var priorCard = Page.Locator(
                ".govuk-summary-card__title:has-text(\"Their last search on this site\")");
            await priorCard.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
            Assert.Equal(1, await priorCard.CountAsync());

            var hitLinks = Page.Locator(".govuk-summary-card__content ol.govuk-list--number li a");
            var linkCount = await hitLinks.CountAsync();
            Assert.True(linkCount > 0, "Prior-search panel rendered but no hit links present.");
            var targets = await hitLinks.EvaluateAllAsync<string[]>(
                "els => els.map(e => e.getAttribute('target'))");
            Assert.All(targets, t => Assert.Equal("_blank", t));

            await Page.StabiliseAsync();
            await SaveScreenshotAsync("admin-message-detail.png");
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(Fixture);
        }
    }

    // --- Helpers ---

    // Drives the anonymous feedback flow through a fresh browser context so the search + POST
    // ride the same session cookie. Uses Playwright's cookie-aware context rather than the
    // shared HttpClient because the feedback form scopes session id from HttpContext.Session,
    // which the SeedClient does not participate in.
    private async Task PostFeedbackAsync(string query, string whatLookingFor, string? email)
    {
        await using var userContext = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1440, Height = 900 }
        });
        var userPage = await userContext.NewPageAsync();

        // Run the search so the sink writes a search_event row keyed by the session id, then
        // post the feedback that the admin will later review.
        var search = await userPage.GotoAsync($"{Fixture.BaseUrl}/search?q={Uri.EscapeDataString(query)}");
        Assert.NotNull(search);
        Assert.Equal(200, search!.Status);

        var feedback = await userPage.GotoAsync($"{Fixture.BaseUrl}/Search/Feedback");
        Assert.NotNull(feedback);
        Assert.Equal(200, feedback!.Status);

        await userPage.Locator("#WhatLookingFor").FillAsync(whatLookingFor);
        if (!string.IsNullOrEmpty(email))
        {
            await userPage.Locator("#Email").FillAsync(email);
        }

        await userPage.Locator("button.govuk-button:has-text(\"Send\")").ClickAsync();
        await userPage.WaitForURLAsync("**/Search/Feedback/Confirmation");
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

    // Handoff screenshots — path-safe under the test project so the human reviewer can browse
    // them from git.
    private Task SaveScreenshotAsync(string filename) => SaveScreenshotAsync(Page, filename);

    private static async Task SaveScreenshotAsync(IPage page, string filename)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Snapshots", "search-ux", "round3");
        Directory.CreateDirectory(dir);
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(dir, filename),
            FullPage = true,
        });
    }
}
