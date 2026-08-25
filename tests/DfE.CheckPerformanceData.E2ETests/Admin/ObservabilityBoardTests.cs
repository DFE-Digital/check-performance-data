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

    // --- A non-admin cannot reach the dashboard (obfuscated as 404) ---

    [Fact]
    public async Task Dashboard_AsNonAdmin_Returns_NotFound()
    {
        try
        {
            await AuthHelpers.ImpersonateAsUnprivilegedUserAsync(_fixture);

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{_fixture.BaseUrl}{DashboardPath}");

            var response = await TestHttpClients.SendAsync(request);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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
public sealed class ObservabilityBoardBrowserTests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    // start() closes the feed the previous engine owned so a board root only ever has one writer.
    // The guard reads a handle the function has to record when it subscribes; when that recording
    // was missing the guard could never fire, the live EventSource stayed connected, and two
    // engines wrote into the same transitions list. That is unobservable until something else
    // writes to the DOM at the wrong moment, so pin it directly rather than through its symptom.
    [Fact]
    public async Task Dashboard_StartingASecondFeed_ClosesTheFirst_SoOneWriterOwnsTheBoard()
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

            var handover = await Page.EvaluateAsync<FeedHandover>(@"() => {
                const root = document.querySelector('[data-obs-board]');
                const url = root.getAttribute('data-stream-url') || '/admin/observability/stream';

                // Own a live feed, keeping hold of whatever start() recorded for it.
                window.ObservabilityBoard.start(root, window.ObservabilityBoard.liveFeed(url));
                const firstHandle = root.__obsEs;

                // Hand the root to a feed that owns nothing; the live one must be closed.
                window.ObservabilityBoard.start(root, { subscribe: () => {} });

                return {
                    recordedFirstFeed: !!firstHandle,
                    // EventSource.CLOSED === 2
                    firstFeedClosed: firstHandle ? firstHandle.readyState === 2 : false,
                    handleClearedForFeedThatOwnsNothing: !root.__obsEs
                };
            }");

            Assert.True(handover.RecordedFirstFeed,
                "start() must record the subscription it opened, or the close-previous guard can never fire");
            Assert.True(handover.FirstFeedClosed,
                "starting a second feed must close the first, or two engines write to one board");
            Assert.True(handover.HandleClearedForFeedThatOwnsNothing,
                "a feed with nothing to close must clear the handle rather than leave a stale one");
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(Fixture);
        }
    }

    // Playwright instantiates this by reflection, so it is a plain settable class.
    public sealed class FeedHandover
    {
        public bool RecordedFirstFeed { get; set; }
        public bool FirstFeedClosed { get; set; }
        public bool HandleClearedForFeedThatOwnsNothing { get; set; }
    }

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

            // Take the board over with a feed that publishes nothing, then drive one controlled
            // snapshot through it.
            //
            // Order matters. start() closes the feed the previous engine owned, so calling it
            // FIRST disconnects the live stream; only then is it safe to clear the DOM, because
            // nothing can write into it afterwards. The previous version cleared first and started
            // second, leaving a window in which a real transition could repopulate the region — on
            // a busy environment that is exactly what happened, and the assertion below read a real
            // request instead of this snapshot.
            //
            // No sleep: start() returns a live engine synchronously and onSnapshot renders
            // synchronously, so there is nothing to wait for.
            await Page.EvaluateAsync(@"() => {
                const root = document.querySelector('[data-obs-board]');
                const engine = window.ObservabilityBoard.start(root, { subscribe: () => {} });

                for (const token of root.querySelectorAll('.obs-board__token')) {
                    token.remove();
                }
                const liveRegion = root.querySelector('[data-obs-transitions]');
                if (liveRegion) liveRegion.textContent = '';

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
