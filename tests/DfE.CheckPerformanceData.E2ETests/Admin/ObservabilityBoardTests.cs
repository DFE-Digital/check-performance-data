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
        "Zendesk ticket",
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

    // --- The board ships an accessible pipeline-state matrix (a govuk-table of recent messages by
    //     stage), which is both the visual record and the textual parallel to the animation ---

    [Fact]
    public async Task Dashboard_RendersPipelineStateMatrix()
    {
        var body = await LoadDashboardAsAdminAsync();

        // The matrix replaces the old summary-list + recent-transitions list: a real table whose
        // body the engine fills live, with stage columns and queue-page links (new tab).
        Assert.Contains("obs-board__parallel", body);
        Assert.Contains("Recent submissions", body);
        Assert.Contains("data-obs-grid", body);
        Assert.Contains("Zendesk ticket", body);
        Assert.Contains("/admin/queues/list/rules-engine", body);
    }

    // --- The export CTA and the board/export scripts are present and wired ---

    [Fact]
    public async Task Dashboard_RendersExportCta_AndWiresBoardAndExportScripts()
    {
        var body = await LoadDashboardAsAdminAsync();

        // Export this view is now a single Excel workbook (four tabs, one per chart, each with an
        // embedded chart); the CSV remains as a secondary data download. Both are server-side links.
        Assert.Contains("/admin/observability/export.xlsx", body);
        Assert.Contains("Export this view (Excel)", body);
        Assert.Contains("/admin/observability/export.csv", body);

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

            // The board root and the pipeline-state matrix are bound in the DOM.
            Assert.Equal(1, await Page.Locator("[data-obs-board]").CountAsync());
            var grid = Page.Locator("[data-obs-grid]");
            Assert.Equal(1, await grid.CountAsync());

            // Drive snapshots through the engine directly (the live SSE feed depends on traffic that
            // may not exist in the test environment). The first snapshot primes the baseline (the
            // matrix records it but it is not re-animated on load); a later snapshot with a NEW
            // reference animates a focusable, labelled token — the board's behavioural contract.
            await Page.EvaluateAsync(@"() => {
                const root = document.querySelector('[data-obs-board]');
                const engine = window.ObservabilityBoard.start(root, { subscribe: () => {} });
                engine.onSnapshot({
                    depths: [{ queueName: 'rules-engine', depth: 2 }],
                    recentTransitions: [
                        { referenceNumber: 'REF-1042', stage: 'RulesEvaluated', decisionStatus: 'AutoApproved' }
                    ]
                });
                engine.onSnapshot({
                    depths: [],
                    recentTransitions: [
                        { referenceNumber: 'REF-2055', stage: 'TicketCreated', decisionStatus: 'AutoApproved' }
                    ]
                });
            }");

            // The matrix now records both messages.
            await Assertions.Expect(grid).ToContainTextAsync("REF-1042");
            await Assertions.Expect(grid).ToContainTextAsync("REF-2055");

            // A token (each message renders as an anchored SVG envelope, class .obs-board__token) was
            // rendered for the new reference, is keyboard-focusable (tabindex), exposes a button role,
            // and carries an accessible name naming the message and inviting inspection.
            var token = Page.Locator(".obs-board__token").First;
            Assert.Equal("0", await token.GetAttributeAsync("tabindex"));
            Assert.Equal("button", await token.GetAttributeAsync("role"));

            // The envelope is an inline SVG inside the token (not a round dot), so the message shape
            // is conveyed by icon, not colour alone.
            Assert.Equal(1, await token.Locator("svg").CountAsync());

            var name = await token.GetAttributeAsync("aria-label");
            Assert.Contains("REF-2055", name);
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
