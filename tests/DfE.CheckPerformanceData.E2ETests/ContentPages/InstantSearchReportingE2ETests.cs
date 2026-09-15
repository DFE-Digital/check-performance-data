using System.Text.RegularExpressions;
using System.Web;
using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;

namespace DfE.CheckPerformanceData.E2ETests.ContentPages;

// When an instant search reports itself, and what it says.
//
// The rule that matters is "once per settled query, never per keystroke": typing a word must
// produce one report, not one per letter. The second rule is that abandonment counts —
// someone who is shown three sections, takes none of them and types something else has told
// us something a selection-only signal would throw away.
//
// Reports are captured at the network boundary rather than read back from the dashboard so
// the trigger semantics are asserted deterministically, without waiting on the background
// writer's drain. One test does follow a report all the way to the admin dashboard.
[Collection("E2E")]
public sealed class InstantSearchReportingE2ETests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    private readonly List<Guid> _createdPages = [];
    private readonly List<Dictionary<string, string>> _reports = [];

    public override async Task DisposeAsync()
    {
        for (var i = _createdPages.Count - 1; i >= 0; i--)
        {
            await CmsSeedHelpers.TryDeletePageAsync(Fixture.SeedClient, _createdPages[i]);
        }
        await base.DisposeAsync();
    }

    // Records every analytics report the page fires, and lets it through to the real endpoint.
    private async Task CaptureReportsAsync()
    {
        await Page.RouteAsync("**/search/instant-analytics", async route =>
        {
            var body = route.Request.PostData;
            if (body is not null)
            {
                var parsed = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var pair in body.Split('&'))
                {
                    var bits = pair.Split('=', 2);
                    if (bits.Length == 2)
                        parsed[HttpUtility.UrlDecode(bits[0])] = HttpUtility.UrlDecode(bits[1]);
                }
                lock (_reports) _reports.Add(parsed);
            }
            await route.ContinueAsync();
        });
    }

    private async Task<string> SeedPageAsync(string searchIn, string scope = "")
    {
        var segment = $"e2e-report-{Guid.NewGuid():N}";
        var id = await CmsSeedHelpers.CreatePageNodeAsync(
            Fixture.SeedClient, CmsSeedHelpers.HelpRootId, "content", segment, "E2E instant reporting");
        _createdPages.Add(id);

        await CmsSeedHelpers.AddWidgetAsync(Fixture.SeedClient, id, "0.0", "search");
        await CmsSeedHelpers.UpdateWidgetAsync(Fixture.SeedClient, id, "0.0", "search",
            new Dictionary<string, string>
            {
                ["label"] = "Search this page",
                ["action"] = "/search",
                ["buttonText"] = "Search",
                ["scope"] = scope,
                ["searchIn"] = searchIn,
                ["instant"] = "true",
                ["noResultsText"] = "No matches",
            });
        await CmsSeedHelpers.AddWidgetAsync(Fixture.SeedClient, id, "0.1", "heading");
        await CmsSeedHelpers.UpdateWidgetAsync(Fixture.SeedClient, id, "0.1", "heading",
            new Dictionary<string, string> { ["level"] = "2", ["text"] = "Providing evidence" });
        await CmsSeedHelpers.AddWidgetAsync(Fixture.SeedClient, id, "0.2", "richtext");
        await CmsSeedHelpers.UpdateWidgetAsync(Fixture.SeedClient, id, "0.2", "richtext",
            new Dictionary<string, string> { ["html"] = "<p>Attach a document.</p>" });
        await CmsSeedHelpers.AddWidgetAsync(Fixture.SeedClient, id, "0.3", "heading");
        await CmsSeedHelpers.UpdateWidgetAsync(Fixture.SeedClient, id, "0.3", "heading",
            new Dictionary<string, string> { ["level"] = "2", ["text"] = "Uploading files" });
        await CmsSeedHelpers.PublishDraftAsync(Fixture.SeedClient, id);

        return $"/help/{segment}";
    }

    // Same shape as the admin suites use: mirror an impersonation cookie into the browser
    // context so the assertions run as that role.
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

    private ILocator Input => Page.Locator("input.autocomplete__input");

    // Tab out rather than clicking something on the page: the suggestion menu is an overlay,
    // so with it open a click aimed at the content underneath lands on the menu instead.
    private async Task SettleByLeavingTheBoxAsync()
    {
        await Page.Keyboard.PressAsync("Tab");
        await Expect(Input).Not.ToBeFocusedAsync();
    }


    private async Task WaitForReportsAsync(int atLeast)
    {
        for (var i = 0; i < 50; i++)
        {
            lock (_reports) if (_reports.Count >= atLeast) return;
            await Task.Delay(100);
        }
        lock (_reports)
            Assert.Fail($"Expected at least {atLeast} report(s); saw {_reports.Count}.");
    }

    // ============================================================
    // 1. Typing a word is one report, not one per letter.
    // ============================================================
    [Fact]
    public async Task TypingAWord_ReportsOnce_NotPerKeystroke()
    {
        var url = await SeedPageAsync("page");
        await CaptureReportsAsync();
        await Page.GotoAsync($"{Fixture.BaseUrl}{url}");

        await Input.ClickAsync();
        await Input.PressSequentiallyAsync("evidence", new LocatorPressSequentiallyOptions { Delay = 40 });
        await Expect(Page.Locator("li.autocomplete__option").First).ToBeVisibleAsync();

        // Blur settles the query.
        await SettleByLeavingTheBoxAsync();
        await WaitForReportsAsync(1);

        lock (_reports)
        {
            Assert.Single(_reports);
            Assert.Equal("evidence", _reports[0]["Q"]);
            Assert.Equal("instant-page", _reports[0]["Surface"]);
        }
    }

    // ============================================================
    // 2. Shown-and-abandoned: the menu appeared, nothing was taken, the person retyped.
    // ============================================================
    [Fact]
    public async Task AQueryAbandonedForADifferentOne_IsReportedWithNoSelection()
    {
        var url = await SeedPageAsync("page");
        await CaptureReportsAsync();
        await Page.GotoAsync($"{Fixture.BaseUrl}{url}");

        await Input.ClickAsync();
        await Input.FillAsync("evidence");
        await Expect(Page.Locator("li.autocomplete__option").First).ToBeVisibleAsync();

        // A query that is not a continuation of the first — this is the person giving up on it.
        await Input.FillAsync("uploading");
        await WaitForReportsAsync(1);

        lock (_reports)
        {
            var abandoned = _reports[0];
            Assert.Equal("evidence", abandoned["Q"]);
            Assert.False(abandoned.ContainsKey("SelectedKey") && abandoned["SelectedKey"].Length > 0);
            Assert.Contains("Providing evidence", abandoned["Shown"]);
        }
    }

    // ============================================================
    // 2b. Abandoning by navigating away, without blurring the box or picking anything. The
    //     most ordinary way someone gives up on a search.
    //
    //     Asserted through the dashboard rather than at the network boundary: this report is
    //     sent with sendBeacon while the page unloads, and route interception is torn down
    //     with the page before it can see it. The claim worth testing is that the row arrives
    //     anyway, which is what the admin surface answers.
    // ============================================================
    [Fact]
    public async Task AQueryAbandonedByLeavingThePage_IsStillReported()
    {
        var term = "cypdnav" + Guid.NewGuid().ToString("N")[..10].ToLowerInvariant();
        var url = await SeedPageAsync("page");
        await Page.GotoAsync($"{Fixture.BaseUrl}{url}");

        await Input.ClickAsync();
        await Input.FillAsync(term);
        await Expect(Page.Locator(".autocomplete__menu")).ToContainTextAsync("No matches");

        // Leave, without blurring the box or choosing anything.
        await Page.GotoAsync($"{Fixture.BaseUrl}/guidance");

        try
        {
            var adminCookie = await AuthHelpers.ImpersonateAsAdminAsync(Fixture);
            AttachCookieToContext(adminCookie);

            var drillIn = $"{Fixture.BaseUrl}/admin/Search/OnPage?path={Uri.EscapeDataString(url)}&range=24h";
            for (var attempt = 0; attempt < 30; attempt++)
            {
                await Page.GotoAsync(drillIn);
                var body = await Page.Locator("body").InnerTextAsync();
                if (body.Contains(term, StringComparison.Ordinal)) return;
                await Page.WaitForTimeoutAsync(500);
            }

            Assert.Fail($"Abandoned query '{term}' never reached the dashboard for {url}.");
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(Fixture);
        }
    }

    // ============================================================
    // 3. What was shown, and what was chosen, both reach the report.
    // ============================================================
    [Fact]
    public async Task ChoosingASection_ReportsTheSelectionAndItsPosition()
    {
        var url = await SeedPageAsync("page");
        await CaptureReportsAsync();
        await Page.GotoAsync($"{Fixture.BaseUrl}{url}");

        await Input.ClickAsync();
        await Input.FillAsync("evidence");
        await Expect(Page.Locator("li.autocomplete__option").First).ToBeVisibleAsync();
        await Page.Locator("li.autocomplete__option").First.ClickAsync();

        await WaitForReportsAsync(1);

        lock (_reports)
        {
            var chosen = _reports[0];
            Assert.Equal("evidence", chosen["Q"]);
            Assert.Equal("1", chosen["SelectedPosition"]);
            Assert.StartsWith("#", chosen["SelectedKey"]);
            Assert.Contains("\"kind\":\"section\"", chosen["Shown"]);
        }
    }

    // ============================================================
    // 4. A query that showed nothing is still worth knowing about.
    // ============================================================
    [Fact]
    public async Task AQueryThatShowedNothing_IsReportedWithAnEmptyList()
    {
        var url = await SeedPageAsync("page");
        await CaptureReportsAsync();
        await Page.GotoAsync($"{Fixture.BaseUrl}{url}");

        await Input.ClickAsync();
        await Input.FillAsync("zzzqqqnothing");
        await Expect(Page.Locator(".autocomplete__menu")).ToContainTextAsync("No matches");
        await SettleByLeavingTheBoxAsync();

        await WaitForReportsAsync(1);

        lock (_reports)
        {
            Assert.Equal("zzzqqqnothing", _reports[0]["Q"]);
            Assert.Equal("[]", _reports[0]["Shown"]);
        }
    }

    // ============================================================
    // 5. An on-page search says which page it ran on — that is the whole point of the
    //    single-page section on the dashboard.
    // ============================================================
    [Fact]
    public async Task AnOnPageSearch_ReportsThePageItRanOn()
    {
        var url = await SeedPageAsync("page");
        await CaptureReportsAsync();
        await Page.GotoAsync($"{Fixture.BaseUrl}{url}");

        await Input.ClickAsync();
        await Input.FillAsync("evidence");
        await Expect(Page.Locator("li.autocomplete__option").First).ToBeVisibleAsync();
        await Page.Locator("li.autocomplete__option").First.ClickAsync();
        await WaitForReportsAsync(1);

        lock (_reports) Assert.Equal(url, _reports[0]["HostPath"]);
    }

    // ============================================================
    // 6. A site-scoped instant search reports as its own surface, with no host page.
    // ============================================================
    [Fact]
    public async Task ASectionScopedInstantSearch_ReportsAsTheInstantSurface()
    {
        var url = await SeedPageAsync("path", scope: "help");
        await CaptureReportsAsync();
        await Page.GotoAsync($"{Fixture.BaseUrl}{url}");

        await Input.ClickAsync();
        await Input.FillAsync("evidence");
        await Page.WaitForTimeoutAsync(600);
        await SettleByLeavingTheBoxAsync();
        await WaitForReportsAsync(1);

        lock (_reports)
        {
            Assert.Equal("instant", _reports[0]["Surface"]);
            Assert.Equal("help", _reports[0]["Scope"]);
            Assert.False(_reports[0].TryGetValue("HostPath", out var h) && h.Length > 0);
        }
    }

    // ============================================================
    // 7. A report reaches the analytics store, not just the wire. Asserted through the
    //    session drill-in — the surface support actually uses when someone quotes their
    //    session id — because the top-queries tables rank by count, where a one-off term
    //    could sit on any page.
    // ============================================================
    [Fact]
    public async Task AReportedQuery_ReachesTheSessionHistoryOnTheDashboard()
    {
        var token = "cypdrep" + Guid.NewGuid().ToString("N")[..10].ToLowerInvariant();
        var url = await SeedPageAsync("page");
        await Page.GotoAsync($"{Fixture.BaseUrl}{url}");

        await Input.ClickAsync();
        await Input.FillAsync(token);
        await Expect(Page.Locator(".autocomplete__menu")).ToContainTextAsync("No matches");
        await SettleByLeavingTheBoxAsync();

        // The feedback form shows the visitor their own session id, which is how support
        // finds a history in the first place.
        await Page.GotoAsync($"{Fixture.BaseUrl}/Search/Feedback");
        var sessionId = await Page.Locator("#SessionIdDisplayOnly").InputValueAsync();
        Assert.False(string.IsNullOrWhiteSpace(sessionId));

        try
        {
            var adminCookie = await AuthHelpers.ImpersonateAsAdminAsync(Fixture);
            AttachCookieToContext(adminCookie);

            // The sink drains on a timer, so the row takes a moment to land.
            for (var attempt = 0; attempt < 30; attempt++)
            {
                await Page.GotoAsync($"{Fixture.BaseUrl}/admin/Search/Session/{sessionId}");
                var body = await Page.Locator("body").InnerTextAsync();
                if (body.Contains(token, StringComparison.Ordinal)) return;
                await Page.WaitForTimeoutAsync(500);
            }

            Assert.Fail($"Instant-search query '{token}' never reached session {sessionId} on the dashboard.");
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(Fixture);
        }
    }
}
