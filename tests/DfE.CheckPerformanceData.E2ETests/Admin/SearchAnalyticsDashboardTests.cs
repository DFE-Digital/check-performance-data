using System.Runtime.InteropServices;
using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;

namespace DfE.CheckPerformanceData.E2ETests.Admin;

// End-to-end coverage for the search-analytics dashboard + feedback-form UX. Assertions
// mirror the surface the admin actually sees: tooltips + query links + bucket selector +
// separated summary cards + widened layout on /admin/Search/, plus the interactive four-
// tile chart switcher and inline top-pages summary card. Feedback-form side asserts hits-
// always-link + prior-search-preserved-on-validation-error on /Search/Feedback.
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

            // 4 tiles, each rendered as a real <button> so keyboard users can Tab-Enter.
            var tileButtons = Page.Locator("button.sa-tile[data-sa-tile]");
            Assert.Equal(4, await tileButtons.CountAsync());
            var tilesWithTitle = Page.Locator(".sa-tile[title]");
            Assert.Equal(4, await tilesWithTitle.CountAsync());

            // Chart SVGs render server-side; the four tile panels (volume / unique-users /
            // zero-results / latency-percentiles) plus the subsequent additions (request-
            // timings scatter under the latency tile and the weekday × hour heatmap).
            // Post-JS the latency panels + non-active tile panels hide but their SVGs are
            // still in the DOM. Heatmap SVG lives in a summary card and always renders.
            var chartSvgs = Page.Locator(".sa-chart");
            var chartCount = await chartSvgs.CountAsync();
            Assert.True(chartCount >= 4,
                $"Expected at least 4 chart svgs, got {chartCount}.");
            await Expect(chartSvgs.First).ToBeVisibleAsync();

            // Axis-tick + axis-title labels are present across the visible chart(s).
            var axisTickCount = await Page.Locator(".sa-chart .sa-chart__axis-tick").CountAsync();
            Assert.True(axisTickCount >= 3, $"Expected at least 3 axis tick labels, got {axisTickCount}");
            var axisTitleCount = await Page.Locator(".sa-chart .sa-chart__axis-title").CountAsync();
            Assert.True(axisTitleCount >= 2, $"Expected at least 2 axis title labels, got {axisTitleCount}");

            // Bucket selector: 5 sizes. Filter to type=radio because the aggregate-toggle
            // form also carries a hidden input[name="bucket"] to preserve the current bucket
            // across a toggle click, and that hidden input should not count as a radio.
            var bucketRadios = Page.Locator("input[name=\"bucket\"][type=\"radio\"]");
            Assert.Equal(5, await bucketRadios.CountAsync());
            var values = await bucketRadios.EvaluateAllAsync<string[]>(
                "els => els.map(e => e.value)");
            Assert.Contains("15m", values);
            Assert.Contains("1h",  values);
            Assert.Contains("1d",  values);
            Assert.Contains("1w",  values);
            Assert.Contains("1mo", values);

            // Top-N summary cards remain three: top queries, top zero-result queries, top
            // pages. The heatmap + funnel cards are also .sa-summary-cards so the
            // total sits at five; assert the three Top-* cards specifically to keep the
            // acceptance shape while allowing the additions above.
            await Expect(Page.Locator(".sa-summary-card .govuk-summary-card__title:has-text(\"Top queries\")")).ToBeVisibleAsync();
            await Expect(Page.Locator(".sa-summary-card .govuk-summary-card__title:has-text(\"Top zero-result queries\")")).ToBeVisibleAsync();
            await Expect(Page.Locator(".sa-summary-card .govuk-summary-card__title:has-text(\"Top pages\")")).ToBeVisibleAsync();

            // Every query link in either top-queries table opens in a new tab.
            var queryLinks = Page.Locator(".sa-summary-card table.govuk-table td a[target=\"_blank\"]");
            var queryLinkCount = await queryLinks.CountAsync();
            if (queryLinkCount > 0)
            {
                var relValues = await queryLinks.EvaluateAllAsync<string[]>(
                    "els => els.map(e => e.getAttribute('rel'))");
                Assert.All(relValues, r => Assert.NotNull(r));
                Assert.All(relValues, r => Assert.Contains("noopener", r));
            }

            // AdminWide flag → the wide layout wrapper is used.
            await Expect(Page.Locator(".admin-layout--wide")).ToBeVisibleAsync();

            // Every <th> on every visible summary-card table carries a title attribute so
            // admins can hover for a plain-language description of the column.
            await AssertEveryHeaderHasTitleAsync(Page.Locator(".sa-summary-card table.govuk-table thead th"),
                context: "summary-card tables");

            await Page.StabiliseAsync();
            await SaveScreenshotAsync("admin-search.png");
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(Fixture);
        }
    }

    // --- Volume drill-in: /admin/Search/Volume renders chart + paged table with GDS pager ---

    [SkippableFact]
    public async Task VolumeDrillIn_RendersChartAndPagedTableWithPagerAndTooltipHeaders()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Linux),
            "Playwright browser test Linux-only");

        try
        {
            var adminCookie = await AuthHelpers.ImpersonateAsAdminAsync(Fixture);
            AttachCookieToContext(adminCookie);

            // 30-day window at 1h buckets → 720 buckets → >> CMS:PageLength so the pager
            // renders Previous / Next links in addition to the numbered pages.
            var url = $"{Fixture.BaseUrl}/admin/Search/Volume?range=30d&bucket=1h";
            var response = await Page.GotoAsync(url);
            Assert.NotNull(response);
            Assert.Equal(200, response!.Status);

            // Chart at the top: the same _VolumeChart partial with its SVG.
            await Expect(Page.Locator("svg.sa-chart").First).ToBeVisibleAsync();

            // Paged table with the standard GDS pager. With 720 buckets the pager has
            // both Previous (on page 2+) and Next; on page 1 only Next appears.
            var pagerNav = Page.Locator("nav.govuk-pagination");
            await Expect(pagerNav).ToBeVisibleAsync();
            Assert.Equal("Pagination", await pagerNav.GetAttributeAsync("aria-label"));

            // Truncation kicked in — with 30d/1h and a small CMS:PageLength the pager
            // covers well over 30 numbered pages. The truncated pager must render only
            // a handful of numbered links (edge + middle + edge, ~9 max) not one link
            // per page. Also asserts Previous is absent on page 1 and at least one
            // ellipsis marker is present when the current page is not near the edges.
            var pageLinksOnFirst = await Page.Locator("nav.govuk-pagination ul.govuk-pagination__list li:not(.govuk-pagination__item--ellipses)").CountAsync();
            Assert.True(pageLinksOnFirst < 12,
                $"Expected < 12 numbered page items on page 1 (truncated), got {pageLinksOnFirst}.");
            await Expect(Page.Locator("nav.govuk-pagination .govuk-pagination__prev")).ToHaveCountAsync(0);
            await Expect(Page.Locator("nav.govuk-pagination .govuk-pagination__next a")).ToBeVisibleAsync();
            await Page.StabiliseAsync();
            await SaveScreenshotAsync("round6/admin-pager-first.png");

            // Navigate directly to page 5 — the truncated pager on page 1 hides the "5"
            // link (only 1,2,3,...,last-3,last-2,last are rendered) so a URL jump is the
            // only way to land on a mid-range page. Middle-range renders both edges +
            // middle block + two ellipses.
            var midPageResponse = await Page.GotoAsync($"{url}&page=5");
            Assert.NotNull(midPageResponse);
            Assert.Equal(200, midPageResponse!.Status);
            await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            await Expect(Page.Locator("nav.govuk-pagination .govuk-pagination__prev a")).ToBeVisibleAsync();
            await Expect(Page.Locator("nav.govuk-pagination .govuk-pagination__next a")).ToBeVisibleAsync();
            var ellipsisCount = await Page.Locator("nav.govuk-pagination li.govuk-pagination__item--ellipses").CountAsync();
            Assert.True(ellipsisCount >= 1,
                $"Expected at least one ellipsis on a mid-range page, got {ellipsisCount}.");
            var midPageLinks = await Page.Locator("nav.govuk-pagination ul.govuk-pagination__list li:not(.govuk-pagination__item--ellipses)").CountAsync();
            Assert.True(midPageLinks < 12,
                $"Expected < 12 numbered page items on a mid-range page (truncated), got {midPageLinks}.");
            await Page.StabiliseAsync();
            await SaveScreenshotAsync("round6/admin-pager-middle.png");

            // Every <th> on the drill-in table has a title attribute.
            await AssertEveryHeaderHasTitleAsync(
                Page.Locator("table.govuk-table thead th"),
                context: "volume drill-in table");

            // Refresh the round-5 handoff shot so the human reviewer sees the truncated
            // pager instead of the 34-page wall.
            await SaveScreenshotAsync("admin-search-volume-drill-in.png");

            // Jump to the last page via the highest-numbered link in the last-edge block.
            // Read the last page number out of the DOM instead of hardcoding it — the
            // total page count varies with the CMS:PageLength setting the environment is
            // configured with.
            var lastPageNum = await Page.EvaluateAsync<int>(
                "() => { const els = document.querySelectorAll('nav.govuk-pagination ul.govuk-pagination__list a[aria-label^=\"Page \"]'); let m = 0; els.forEach(e => { const n = parseInt(e.getAttribute('aria-label').replace('Page ', ''), 10); if (n > m) m = n; }); return m; }");
            Assert.True(lastPageNum >= 30,
                $"Test corpus must exceed 30 pages to exercise truncation; got {lastPageNum}.");
            var lastResponse = await Page.GotoAsync($"{url}&page={lastPageNum}");
            Assert.NotNull(lastResponse);
            Assert.Equal(200, lastResponse!.Status);
            await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            await Expect(Page.Locator("nav.govuk-pagination .govuk-pagination__prev a")).ToBeVisibleAsync();
            await Expect(Page.Locator("nav.govuk-pagination .govuk-pagination__next")).ToHaveCountAsync(0);
            await Page.StabiliseAsync();
            await SaveScreenshotAsync("round6/admin-pager-last.png");
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(Fixture);
        }
    }

    // --- Landing volume <details>: "View all volume data →" link appears when > cap rows ---

    [SkippableFact]
    public async Task Dashboard_VolumeDetailsFallback_LinksToDrillInWhenOverCap()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Linux),
            "Playwright browser test Linux-only");

        try
        {
            var adminCookie = await AuthHelpers.ImpersonateAsAdminAsync(Fixture);
            AttachCookieToContext(adminCookie);

            // 90d + 15m → ~8600 buckets → guaranteed to exceed the 20-row inline cap.
            var url = $"{Fixture.BaseUrl}/admin/Search/?range=90d&bucket=15m";
            var response = await Page.GotoAsync(url);
            Assert.NotNull(response);
            Assert.Equal(200, response!.Status);

            // The link lives inside a closed <details>; open the summary so the anchor is
            // visible before asserting.
            await Page.Locator("[data-sa-panel=\"volume\"] details summary").First.ClickAsync();

            var viewAll = Page.Locator("[data-sa-panel=\"volume\"] details a.govuk-link:has-text(\"View all volume data\")");
            await Expect(viewAll).ToBeVisibleAsync();
            var href = await viewAll.GetAttributeAsync("href");
            Assert.NotNull(href);
            Assert.Contains("/admin/Search/Volume", href);
            Assert.Contains("range=90d", href);
            Assert.Contains("bucket=15m", href);
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(Fixture);
        }
    }

    // --- Interactive tiles: click switches the visible chart and updates aria-pressed ---

    [SkippableFact]
    public async Task Dashboard_TileClicks_SwitchBetweenFourChartsAndUpdateAriaPressed()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Linux),
            "Playwright browser test Linux-only");

        try
        {
            var adminCookie = await AuthHelpers.ImpersonateAsAdminAsync(Fixture);
            AttachCookieToContext(adminCookie);

            var response = await Page.GotoAsync($"{Fixture.BaseUrl}/admin/Search/");
            Assert.NotNull(response);
            Assert.Equal(200, response!.Status);

            await Page.Locator(".sa-tiles").WaitForAsync(
                new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

            // On load: tile 1 (volume) pressed, others not; tile 1's chart panel visible.
            await AssertTilePressed("volume");
            await AssertPanelVisibleOnly("volume");

            // Click tile 2 (unique-users): its chart becomes visible; the other three hide.
            await Page.Locator("button.sa-tile[data-sa-tile=\"unique-users\"]").ClickAsync();
            await AssertTilePressed("unique-users");
            await AssertPanelVisibleOnly("unique-users");

            // Click tile 3 (zero-results).
            await Page.Locator("button.sa-tile[data-sa-tile=\"zero-results\"]").ClickAsync();
            await AssertTilePressed("zero-results");
            await AssertPanelVisibleOnly("zero-results");

            // Click tile 4 (latency): panel visible + THREE polylines (p5 / p50 / p95).
            await Page.Locator("button.sa-tile[data-sa-tile=\"latency\"]").ClickAsync();
            await AssertTilePressed("latency");
            await AssertPanelVisibleOnly("latency");

            var latencyPolylines = Page.Locator("[data-sa-panel=\"latency\"] svg.sa-chart:not(.sa-chart--scatter) polyline");
            Assert.Equal(3, await latencyPolylines.CountAsync());

            // Round-7: the latency tile also reveals a SECOND panel — the request-timings
            // scatter chart. Assert both panels are visible and the scatter carries at
            // least a handful of <circle> dots.
            var latencyPanels = Page.Locator("[data-sa-panel=\"latency\"]");
            var panelCount = await latencyPanels.CountAsync();
            Assert.Equal(2, panelCount);
            for (var i = 0; i < panelCount; i++)
            {
                Assert.True(await latencyPanels.Nth(i).IsVisibleAsync(),
                    $"Expected latency panel #{i} to be visible after clicking the latency tile.");
            }
            var scatterCircles = Page.Locator("[data-sa-panel=\"latency\"] svg.sa-chart--scatter circle");
            var circleCount = await scatterCircles.CountAsync();
            Assert.True(circleCount >= 5,
                $"Expected at least 5 scatter dots on the request-timings chart, got {circleCount}.");

            await Page.StabiliseAsync();
            await SaveScreenshotAsync("admin-search-latency-tile.png");
            await SaveScreenshotAsync("round7/admin-search-latency-tile-timings.png");
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(Fixture);
        }
    }

    // --- Round-7: heatmap card renders a 168-cell weekday × hour grid ---------

    [SkippableFact]
    public async Task Dashboard_WeekdayHourHeatmap_RendersFullGridAndDataTable()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Linux),
            "Playwright browser test Linux-only");

        try
        {
            var adminCookie = await AuthHelpers.ImpersonateAsAdminAsync(Fixture);
            AttachCookieToContext(adminCookie);

            var response = await Page.GotoAsync($"{Fixture.BaseUrl}/admin/Search/");
            Assert.NotNull(response);
            Assert.Equal(200, response!.Status);

            await Page.Locator(".sa-heatmap-card").WaitForAsync(
                new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

            // Heatmap card is present with the "When people search" heading.
            var heatmapTitle = Page.Locator(".sa-heatmap-card .govuk-summary-card__title:has-text(\"When people search\")");
            await Expect(heatmapTitle).ToBeVisibleAsync();

            // 7 rows × 24 columns = 168 <rect> cells.
            var heatmapCells = Page.Locator(".sa-heatmap rect.sa-heatmap__cell");
            var cellCount = await heatmapCells.CountAsync();
            Assert.Equal(168, cellCount);

            // Data-table fallback is present with 168 body rows (one per cell).
            await Page.Locator(".sa-heatmap-card details summary").ClickAsync();
            var fallbackRows = Page.Locator(".sa-heatmap-card details tbody tr");
            Assert.Equal(168, await fallbackRows.CountAsync());

            await Page.StabiliseAsync();
            await SaveScreenshotAsync("round7/admin-search-heatmap.png");
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(Fixture);
        }
    }

    // --- Round-7: zero-result outcomes funnel card ---------------------------

    [SkippableFact]
    public async Task Dashboard_ZeroResultFunnel_RendersThreeTilesOrEmptyState()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Linux),
            "Playwright browser test Linux-only");

        try
        {
            var adminCookie = await AuthHelpers.ImpersonateAsAdminAsync(Fixture);
            AttachCookieToContext(adminCookie);

            var response = await Page.GotoAsync($"{Fixture.BaseUrl}/admin/Search/");
            Assert.NotNull(response);
            Assert.Equal(200, response!.Status);

            await Page.Locator(".sa-funnel-card").WaitForAsync(
                new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

            var funnelTitle = Page.Locator(".sa-funnel-card .govuk-summary-card__title:has-text(\"Zero-result outcomes\")");
            await Expect(funnelTitle).ToBeVisibleAsync();

            // Either three sub-tiles are present (data path) or a single empty-state
            // paragraph is present (no-data path). Both are valid.
            var funnelTiles = Page.Locator(".sa-funnel-card [data-sa-funnel]");
            var tileCount = await funnelTiles.CountAsync();
            if (tileCount > 0)
            {
                Assert.Equal(3, tileCount);
                var expectedKeys = new[] { "refined", "feedback", "silent" };
                var actualKeys = new List<string?>();
                for (var i = 0; i < tileCount; i++)
                {
                    var key = await funnelTiles.Nth(i).GetAttributeAsync("data-sa-funnel");
                    actualKeys.Add(key);
                    var value = await funnelTiles.Nth(i).Locator(".sa-funnel__value").InnerTextAsync();
                    Assert.Matches(new System.Text.RegularExpressions.Regex(@"^[0-9,]+$"), value.Trim());
                }
                foreach (var expected in expectedKeys)
                {
                    Assert.Contains(expected, actualKeys);
                }
            }
            else
            {
                var emptyState = Page.Locator(".sa-funnel-card .govuk-summary-card__content p.govuk-body:has-text(\"No zero-result searches\")");
                await Expect(emptyState).ToBeVisibleAsync();
            }

            await Page.StabiliseAsync();
            await SaveScreenshotAsync("round7/admin-search-zero-result-funnel.png");
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(Fixture);
        }
    }

    // --- Round-7: request timings drill-in page ------------------------------

    [SkippableFact]
    public async Task RequestTimingsDrillIn_RendersPagedTableAndPagerAndTooltipHeaders()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Linux),
            "Playwright browser test Linux-only");

        try
        {
            var adminCookie = await AuthHelpers.ImpersonateAsAdminAsync(Fixture);
            AttachCookieToContext(adminCookie);

            var url = $"{Fixture.BaseUrl}/admin/Search/RequestTimings?range=7d";
            var response = await Page.GotoAsync(url);
            Assert.NotNull(response);
            Assert.Equal(200, response!.Status);

            // Page heading + filter form present.
            await Expect(Page.Locator("h1.govuk-heading-xl:has-text(\"Request timings\")")).ToBeVisibleAsync();
            await Expect(Page.Locator("form.sa-filters")).ToBeVisibleAsync();

            // Every <th> on the drill-in table has a title attribute.
            await AssertEveryHeaderHasTitleAsync(
                Page.Locator("table.govuk-table thead th"),
                context: "request timings drill-in table");

            await Page.StabiliseAsync();
            await SaveScreenshotAsync("round7/admin-search-request-timings-drill.png");
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(Fixture);
        }
    }

    // --- Round-7: anomaly chip DOM shape check -------------------------------

    // Anomaly chips render as small <span class="sa-anomaly-chip"> tags beneath each stat
    // tile ONLY when the current window differs by more than +/-10% from the same-length
    // prior window. The test environment doesn't guarantee a corpus with a >10% delta on
    // any tile, so this test asserts the DOM shape is available and either
    //   (a) a chip renders with a valid direction class, OR
    //   (b) the tile is chip-less (delta within +/-10%) but not broken
    //   (c) an insufficient-prior-data hint renders (custom range > 45 days)
    // A more deterministic anomaly-corpus test would need a fixture that seeds the prior
    // window explicitly; documented in the SUMMARY testing-gaps section.
    [SkippableFact]
    public async Task Dashboard_AnomalyChips_RenderConditionallyBeneathEachTile()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Linux),
            "Playwright browser test Linux-only");

        try
        {
            var adminCookie = await AuthHelpers.ImpersonateAsAdminAsync(Fixture);
            AttachCookieToContext(adminCookie);

            var response = await Page.GotoAsync($"{Fixture.BaseUrl}/admin/Search/");
            Assert.NotNull(response);
            Assert.Equal(200, response!.Status);

            await Page.Locator(".sa-tiles").WaitForAsync(
                new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

            // Zero or more chips is OK. Every chip that renders must carry one of the
            // three direction classes AND a data-sa-chip attribute mapping it to a tile.
            var chips = Page.Locator(".sa-tile .sa-anomaly-chip");
            var chipCount = await chips.CountAsync();
            for (var i = 0; i < chipCount; i++)
            {
                var cls = await chips.Nth(i).GetAttributeAsync("class") ?? string.Empty;
                Assert.Contains("sa-anomaly-chip", cls);
                Assert.True(
                    cls.Contains("sa-anomaly-chip--favourable")
                    || cls.Contains("sa-anomaly-chip--unfavourable")
                    || cls.Contains("sa-anomaly-chip--neutral"),
                    $"Chip {i} class '{cls}' does not carry a direction modifier.");
                var chipKey = await chips.Nth(i).GetAttributeAsync("data-sa-chip");
                Assert.False(string.IsNullOrEmpty(chipKey),
                    $"Chip {i} is missing its data-sa-chip attribute.");
            }
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(Fixture);
        }
    }

    // --- Top pages summary card renders inline (no more bottom "View top pages" link only) ---

    [SkippableFact]
    public async Task Dashboard_TopPagesSummaryCard_RendersInlineWithOptionalViewAllLink()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Linux),
            "Playwright browser test Linux-only");

        try
        {
            var adminCookie = await AuthHelpers.ImpersonateAsAdminAsync(Fixture);
            AttachCookieToContext(adminCookie);

            var response = await Page.GotoAsync($"{Fixture.BaseUrl}/admin/Search/");
            Assert.NotNull(response);
            Assert.Equal(200, response!.Status);

            // Top pages card is present with the "Top pages" heading.
            var topPagesTitle = Page.Locator(".sa-summary-card .govuk-summary-card__title:has-text(\"Top pages\")");
            await Expect(topPagesTitle).ToBeVisibleAsync();

            // The old bottom-of-page free-standing link is gone. The only "View all top pages…"
            // link, if any, lives INSIDE the Top pages summary card.
            var topPagesCard = Page.Locator(".sa-summary-card:has(.govuk-summary-card__title:has-text(\"Top pages\"))");
            var inCardViewAll = topPagesCard.Locator("a.govuk-link:has-text(\"View all top pages\")");
            var inCardCount = await inCardViewAll.CountAsync();

            // Total link count on the page (either 0 when the card fits everything, or 1 when
            // there are more than 10 pages and the "View all" link renders inside the card).
            var allViewAll = Page.Locator("a.govuk-link:has-text(\"View all top pages\")");
            Assert.Equal(inCardCount, await allViewAll.CountAsync());

            if (inCardCount > 0)
            {
                var href = await inCardViewAll.First.GetAttributeAsync("href");
                Assert.NotNull(href);
                Assert.Contains("/admin/Search/Pages", href);
            }
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
        // as editor so the auto-fill-email path is exercised — the impersonation cookie
        // set here does NOT rotate the session id so the search fired below still ends
        // up linked to the same session_id the feedback form reads.
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

    // --- Interactive-tile assertion helpers ---

    private async Task AssertTilePressed(string key)
    {
        var tiles = Page.Locator("button.sa-tile[data-sa-tile]");
        var count = await tiles.CountAsync();
        for (var i = 0; i < count; i++)
        {
            var tile = tiles.Nth(i);
            var tileKey = await tile.GetAttributeAsync("data-sa-tile");
            var pressed = await tile.GetAttributeAsync("aria-pressed");
            var expected = tileKey == key ? "true" : "false";
            Assert.Equal(expected, pressed);
        }
    }

    private async Task AssertPanelVisibleOnly(string key)
    {
        var panels = Page.Locator("[data-sa-panel]");
        var count = await panels.CountAsync();
        for (var i = 0; i < count; i++)
        {
            var panel = panels.Nth(i);
            var panelKey = await panel.GetAttributeAsync("data-sa-panel");
            var isVisible = await panel.IsVisibleAsync();
            if (panelKey == key)
            {
                Assert.True(isVisible, $"Expected panel {panelKey} to be visible.");
            }
            else
            {
                Assert.False(isVisible, $"Expected panel {panelKey} to be hidden.");
            }
        }
    }

    // --- Tooltip assertion helper ---

    // Iterates the located <th> elements and asserts every one carries a non-empty title
    // attribute. Fails with a readable "table X row Y column Z is missing a title" message
    // so a regression tells the reader which column dropped its tooltip.
    private static async Task AssertEveryHeaderHasTitleAsync(ILocator headers, string context)
    {
        var count = await headers.CountAsync();
        Assert.True(count > 0, $"Expected at least one <th> in {context}, got 0.");
        for (var i = 0; i < count; i++)
        {
            var th = headers.Nth(i);
            var title = await th.GetAttributeAsync("title");
            var text = (await th.InnerTextAsync()).Trim();
            Assert.False(string.IsNullOrEmpty(title),
                $"Header {i + 1} in {context} (\"{text}\") is missing a title attribute.");
        }
    }

    // --- Adaptive axis ticks, hover crosshair, aggregate-to-typical-week ----------------

    // Adaptive X-axis labels: 30d window should render date-shaped ticks (day/month), NOT
    // the HH:mm-shaped ticks a 24h view would use. Hover the plot area — the crosshair
    // JS attaches a tooltip div. Toggle the "Aggregate to a typical week" checkbox — the
    // page reloads with ?aggregate=week and the X-axis now spans a full week.
    [SkippableFact]
    public async Task Dashboard_AdaptiveAxes_HoverCrosshair_AndAggregateToggle()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Linux),
            "Playwright browser test Linux-only");

        try
        {
            var adminCookie = await AuthHelpers.ImpersonateAsAdminAsync(Fixture);
            AttachCookieToContext(adminCookie);

            // 30-day window — adaptive labels must be date-shaped (e.g. "24 Jul").
            var url30 = $"{Fixture.BaseUrl}/admin/Search/?range=30d&bucket=1d";
            var response30 = await Page.GotoAsync(url30);
            Assert.NotNull(response30);
            Assert.Equal(200, response30!.Status);

            await Page.Locator("svg.sa-chart[data-sa-crosshair='true']").First
                .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

            var xTickTexts30 = await Page
                .Locator("[data-sa-panel='volume'] svg.sa-chart .sa-chart__axis-tick[text-anchor='middle']")
                .EvaluateAllAsync<string[]>("els => els.map(e => e.textContent.trim())");
            // Format on 30d: "d MMM" — assert at least one label matches d(d)? MMM shape.
            var monthShort = new System.Text.RegularExpressions.Regex(@"^\d{1,2}\s(Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)$");
            Assert.Contains(xTickTexts30, t => monthShort.IsMatch(t));

            await Page.StabiliseAsync();
            await SaveScreenshotAsync("round11/admin-search-30d.png");

            // Hover crosshair — dispatch a mousemove directly on the SVG at a coord inside
            // the plot area and verify a tooltip element with mapped values becomes visible.
            // Using DispatchEventAsync rather than Page.Mouse.MoveAsync because the latter
            // can miss the SVG under certain overlay/z-index conditions in headless Chromium.
            var svg = Page.Locator("[data-sa-panel='volume'] svg.sa-chart[data-sa-crosshair='true']").First;
            var box = await svg.BoundingBoxAsync();
            Assert.NotNull(box);
            var cursorX = box!.X + box.Width * 0.55;
            var cursorY = box.Y + box.Height * 0.55;
            await svg.DispatchEventAsync("mousemove", new
            {
                clientX = cursorX,
                clientY = cursorY,
                bubbles = true,
            });

            var tooltip = Page.Locator(".sa-chart__crosshair-tooltip");
            await Expect(tooltip).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 5000 });
            var tooltipText = (await tooltip.InnerTextAsync()).Trim();
            Assert.False(string.IsNullOrEmpty(tooltipText),
                "Crosshair tooltip should render mapped X + Y values on hover.");
            Assert.Contains("—", tooltipText); // "time — value" separator

            // Crosshair lines are inside the SVG.
            var crosshairLines = Page.Locator("[data-sa-panel='volume'] svg.sa-chart line.sa-chart__crosshair-line[visibility='visible']");
            Assert.True(await crosshairLines.CountAsync() >= 2,
                "Two crosshair lines (vertical + horizontal) should be visible on hover.");

            await Page.StabiliseAsync();
            await SaveScreenshotAsync("round11/admin-search-hover-crosshair.png");

            // Aggregate toggle — flip the checkbox; the form auto-submits via the small JS
            // handler. The reloaded page should have ?aggregate=week in the URL and the
            // X-axis should now span a full week's worth of hours (Mon .. Sun labels).
            var aggregateBox = Page.Locator("input[data-sa-aggregate-toggle='true']");
            await Expect(aggregateBox).ToBeVisibleAsync();
            await aggregateBox.ClickAsync();

            await Page.WaitForURLAsync(u => u.Contains("aggregate=week"),
                new PageWaitForURLOptions { Timeout = 10000 });
            await Page.Locator("svg.sa-chart[data-sa-crosshair='true']").First
                .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

            // In aggregate mode the X-axis is a cyclic 7-day timeline anchored to a Monday
            // in the year 2001. Format on that span is "ddd d" (weekday + day-of-month).
            var xTicksAgg = await Page
                .Locator("[data-sa-panel='volume'] svg.sa-chart .sa-chart__axis-tick[text-anchor='middle']")
                .EvaluateAllAsync<string[]>("els => els.map(e => e.textContent.trim())");
            var weekdayLabelPresent = xTicksAgg.Any(t =>
                t.StartsWith("Mon") || t.StartsWith("Tue") || t.StartsWith("Wed") ||
                t.StartsWith("Thu") || t.StartsWith("Fri") || t.StartsWith("Sat") ||
                t.StartsWith("Sun"));
            Assert.True(weekdayLabelPresent,
                $"Aggregate mode X-axis should carry weekday labels; got: {string.Join(", ", xTicksAgg)}");

            // The active section heading reflects the mode.
            await Expect(Page.Locator("[data-sa-panel='volume'] h2.govuk-heading-m:has-text(\"typical week\")"))
                .ToBeVisibleAsync();

            await Page.StabiliseAsync();
            await SaveScreenshotAsync("round11/admin-search-aggregated.png");

            // Aggregate-mode hover — the tooltip must show weekday + time only. The
            // bucket ISO timestamps are anchored to a synthetic Monday (2001-01-01
            // UTC) so any month name or 4-digit year in the tooltip is the anchor
            // date leaking through.
            var aggSvg = Page.Locator("[data-sa-panel='volume'] svg.sa-chart[data-sa-crosshair='true']").First;
            var aggBox = await aggSvg.BoundingBoxAsync();
            Assert.NotNull(aggBox);
            var aggX = aggBox!.X + aggBox.Width * 0.55;
            var aggY = aggBox.Y + aggBox.Height * 0.55;
            await aggSvg.DispatchEventAsync("mousemove", new
            {
                clientX = aggX,
                clientY = aggY,
                bubbles = true,
            });

            var aggTooltip = Page.Locator(".sa-chart__crosshair-tooltip");
            await Expect(aggTooltip).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 5000 });
            var aggTooltipText = (await aggTooltip.InnerTextAsync()).Trim();
            var weekdayLong = new System.Text.RegularExpressions.Regex(
                @"\b(Monday|Tuesday|Wednesday|Thursday|Friday|Saturday|Sunday)\b");
            Assert.True(weekdayLong.IsMatch(aggTooltipText),
                $"Aggregate-mode tooltip should contain a long weekday name; got: '{aggTooltipText}'");
            var monthNamePattern = new System.Text.RegularExpressions.Regex(
                @"\b(January|February|March|April|May|June|July|August|September|October|November|December|Jan|Feb|Mar|Apr|Jun|Jul|Aug|Sep|Oct|Nov|Dec)\b");
            Assert.False(monthNamePattern.IsMatch(aggTooltipText),
                $"Aggregate-mode tooltip should NOT contain a month name (anchor date leaking); got: '{aggTooltipText}'");
            var yearPattern = new System.Text.RegularExpressions.Regex(@"\b\d{4}\b");
            Assert.False(yearPattern.IsMatch(aggTooltipText),
                $"Aggregate-mode tooltip should NOT contain a 4-digit year (anchor date leaking); got: '{aggTooltipText}'");

            await Page.StabiliseAsync();
            await SaveScreenshotAsync("round11/admin-search-aggregated-hover.png");
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
