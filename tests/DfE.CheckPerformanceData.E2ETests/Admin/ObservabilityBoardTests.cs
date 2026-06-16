using System.Net;
using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;

namespace DfE.CheckPerformanceData.E2ETests.Admin;

// The observability dashboard renders the workflow board server-side: a left-to-right pipeline
// skeleton with the five labelled stage nodes and an accessible textual parallel (counts per
// stage + recent transitions) so the information is available without motion. The export CTA and
// the board/export scripts are wired into the page. All assertions are DOM-level, not pixel.
[Collection("E2E")]
[Trait("Category", "W0")]
public sealed class ObservabilityBoardTests(PlaywrightFixture fixture)
{
    private readonly PlaywrightFixture _fixture = fixture;

    private const string DashboardPath = "/admin/observability";

    private static readonly string[] StageLabels =
    {
        "Submit",
        "Rules-queue",
        "Rules engine",
        "Zendesk-queue",
        "Ticket",
    };

    // --- A non-admin cannot reach the dashboard ---

    [Fact]
    public async Task Dashboard_AsNonAdmin_Redirects_To_AccessDenied()
    {
        try
        {
            await AuthHelpers.ImpersonateAsUnprivilegedUserAsync(_fixture);

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{_fixture.BaseUrl}{DashboardPath}");

            var response = await TestHttpClients.SendAsync(request);

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("AccessDenied", response.Headers.Location?.ToString() ?? string.Empty);
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(_fixture);
        }
    }

    // --- The board skeleton renders the five labelled stage nodes ---

    [Fact]
    public async Task Dashboard_RendersBoardSkeleton_WithFiveStageNodes()
    {
        var body = await LoadDashboardAsAdminAsync();

        Assert.Contains("obs-board", body);

        foreach (var label in StageLabels)
        {
            Assert.Contains(label, body);
        }
    }

    // --- The board ships an accessible textual parallel (counts per stage + recent transitions) ---

    [Fact]
    public async Task Dashboard_RendersAccessibleTextualParallel()
    {
        var body = await LoadDashboardAsAdminAsync();

        // The accessible parallel region carries per-stage counts and the recent transitions list,
        // so a non-visual user gets the same information the animation conveys.
        Assert.Contains("obs-board__parallel", body);
        Assert.Contains("Pipeline state", body);
        Assert.Contains("Recent transitions", body);
    }

    // --- The export CTA and the board/export scripts are present and wired ---

    [Fact]
    public async Task Dashboard_RendersExportCta_AndWiresBoardAndExportScripts()
    {
        var body = await LoadDashboardAsAdminAsync();

        // The export is now a server-side CSV download link (the old client-side chart-PNG path was
        // removed), so the CTA points at the export.csv endpoint and is labelled accordingly.
        Assert.Contains("/admin/observability/export.csv", body);
        Assert.Contains("Export this view (CSV)", body);

        // Both board scripts are still wired: the board engine, and the export helper that drives the
        // print-to-PDF button (the CSV link itself needs no JavaScript).
        Assert.Contains("observability-board.js", body);
        Assert.Contains("observability-export.js", body);
    }

    private async Task<string> LoadDashboardAsAdminAsync()
    {
        try
        {
            await AuthHelpers.ImpersonateAsAdminAsync(_fixture);

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{_fixture.BaseUrl}{DashboardPath}");
            var response = await TestHttpClients.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await response.Content.ReadAsStringAsync();
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(_fixture);
        }
    }
}

// The board's behavioural contract asserted in a real browser: once the live engine has bound to
// the rendered skeleton, the recent-transitions live region is present and animated tokens are
// keyboard-focusable. Assertions are DOM/JS-level, not pixel.
[Collection("E2E")]
[Trait("Category", "W0")]
public sealed class ObservabilityBoardBrowserTests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    [Fact]
    public async Task Dashboard_BoardBinds_LiveRegionPresent_AndTokensAreFocusable()
    {
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

            await Page.GotoAsync($"{Fixture.BaseUrl}/admin/observability");
            await Page.WaitForLoadStateAsync(LoadState.Load);

            // The board root and the recent-transitions live region are bound in the DOM.
            Assert.Equal(1, await Page.Locator("[data-obs-board]").CountAsync());
            var liveRegion = Page.Locator("[data-obs-transitions]");
            Assert.Equal(1, await liveRegion.CountAsync());
            Assert.Equal("polite", await liveRegion.GetAttributeAsync("aria-live"));

            // Drive a snapshot through the engine directly (the live SSE feed depends on traffic
            // that may not exist in the test environment); the engine must render a focusable,
            // labelled token and update the live region — the board's behavioural contract.
            await Page.EvaluateAsync(@"() => {
                const root = document.querySelector('[data-obs-board]');
                const engine = window.ObservabilityBoard.start(root, { subscribe: () => {} });
                engine.onSnapshot({
                    depths: [{ queueName: 'rules-engine', depth: 2 }],
                    recentTransitions: [
                        { referenceNumber: 'REF-1042', stage: 'RulesEvaluated', decisionStatus: 'AutoApproved' }
                    ]
                });
            }");

            // The live region now lists the transition.
            await Assertions.Expect(liveRegion).ToContainTextAsync("REF-1042");

            // A token (the reworked board renders each message as an anchored SVG envelope, still
            // class .obs-board__token) was rendered, is keyboard-focusable (tabindex), exposes a
            // button role, and carries an accessible name naming the message and inviting inspection.
            var token = Page.Locator(".obs-board__token").First;
            Assert.Equal("0", await token.GetAttributeAsync("tabindex"));
            Assert.Equal("button", await token.GetAttributeAsync("role"));

            // The envelope is an inline SVG inside the token (not a round dot), so the message shape
            // is conveyed by icon, not colour alone.
            Assert.Equal(1, await token.Locator("svg").CountAsync());

            var name = await token.GetAttributeAsync("aria-label");
            Assert.Contains("REF-1042", name);
            Assert.Contains("inspect", name, StringComparison.OrdinalIgnoreCase);

            await token.FocusAsync();
            var isFocused = await token.EvaluateAsync<bool>("el => el === document.activeElement");
            Assert.True(isFocused, "Board token should be keyboard-focusable.");
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(Fixture);
        }
    }
}
