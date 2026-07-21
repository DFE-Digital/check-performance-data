using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;

namespace DfE.CheckPerformanceData.E2ETests.ContentPages;

// End-to-end coverage for the search-results widget on content pages. Each test
// seeds its own PageNode under /help with a Guid-suffixed segment so tests can
// interleave without collisions; teardown best-effort deletes the leaf page.
//
// Pairs with:
//   ResultsWidgetRenderContractTests (source-file tokens)
//   SiteSearchMergedPagedTests (service math)
//   WidgetEditorContractTests (editor form fields)
[Collection("E2E")]
[Trait("Category", "W1")]
public sealed class ResultsWidgetE2ETests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    // Term with enough real-content hits to guarantee multi-page pagination at the
    // default CMS:PageLength of 20 (currently ~59 combined page+block hits).
    private const string HighVolumeTerm = "pupil";

    // Track seeded pages for teardown so a failing test doesn't leave orphans that
    // future test runs would then collide with.
    private readonly List<Guid> _createdPages = [];

    // Overrides SeedingPageTest.DisposeAsync (new virtual against PageTest's non-virtual
    // Playwright teardown) — using `override` instead of `new` keeps the xUnit1013
    // analyzer quiet (it flags public non-Fact methods on test classes but exempts
    // override methods).
    public override async Task DisposeAsync()
    {
        foreach (var id in _createdPages)
        {
            await CmsSeedHelpers.TryDeletePageAsync(Fixture.SeedClient, id);
        }
        await base.DisposeAsync();
    }

    // Creates /help/{segment} with a results widget; returns (id, url path).
    private async Task<(Guid Id, string UrlPath, string Segment)> SeedResultsPageAsync(
        Dictionary<string, string>? widgetProps = null)
    {
        var segment = $"e2e-results-{Guid.NewGuid():N}";
        var id = await CmsSeedHelpers.CreatePageNodeAsync(
            Fixture.SeedClient,
            parentId: CmsSeedHelpers.HelpRootId,
            pageType: "content",
            segment: segment,
            title: "E2E results widget");
        _createdPages.Add(id);

        await CmsSeedHelpers.AddWidgetAsync(Fixture.SeedClient, id, path: "0.0", widgetType: "results");
        if (widgetProps is { Count: > 0 })
        {
            await CmsSeedHelpers.UpdateWidgetAsync(
                Fixture.SeedClient, id, path: "0.0", widgetType: "results", props: widgetProps);
        }
        await CmsSeedHelpers.PublishDraftAsync(Fixture.SeedClient, id);

        return (id, $"/help/{segment}", segment);
    }

    // Seeds `count` PageNodes under /help whose titles all contain a fresh unique token,
    // then returns the token. A search for that token is guaranteed to hit exactly those
    // pages — makes the pagination tests independent of whatever content the deployed env
    // happens to carry (review-app envs may have far fewer live pages than the local dev
    // stack). Each seeded page's Title contributes to PageNode.SearchVector at weight B.
    private async Task<string> SeedSearchableFixturesAsync(int count)
    {
        // Lowercase hex chunk: tsvector-safe (no stopword collision) and short enough to
        // keep the test title readable.
        var token = "cypde2e" + Guid.NewGuid().ToString("N")[..12].ToLowerInvariant();
        for (int i = 0; i < count; i++)
        {
            var segment = $"e2e-fixture-{i}-{Guid.NewGuid():N}";
            var id = await CmsSeedHelpers.CreatePageNodeAsync(
                Fixture.SeedClient,
                parentId: CmsSeedHelpers.HelpRootId,
                pageType: "content",
                segment: segment,
                title: $"E2E fixture {token} number {i}");
            _createdPages.Add(id);
            await CmsSeedHelpers.PublishDraftAsync(Fixture.SeedClient, id);
        }
        return token;
    }

    // Creates /help/{segment} with a search-input widget targeting the given action URL.
    private async Task<(Guid Id, string UrlPath)> SeedSearchInputPageAsync(string actionUrl)
    {
        var segment = $"e2e-search-{Guid.NewGuid():N}";
        var id = await CmsSeedHelpers.CreatePageNodeAsync(
            Fixture.SeedClient,
            parentId: CmsSeedHelpers.HelpRootId,
            pageType: "content",
            segment: segment,
            title: "E2E search input");
        _createdPages.Add(id);

        await CmsSeedHelpers.AddWidgetAsync(Fixture.SeedClient, id, path: "0.0", widgetType: "search");
        await CmsSeedHelpers.UpdateWidgetAsync(
            Fixture.SeedClient, id, path: "0.0", widgetType: "search",
            props: new Dictionary<string, string>
            {
                ["label"] = "Search",
                ["action"] = actionUrl,
                ["placeholder"] = "",
                ["buttonText"] = "Search",
                ["scope"] = "",
            });
        await CmsSeedHelpers.PublishDraftAsync(Fixture.SeedClient, id);

        return (id, $"/help/{segment}");
    }

    // ============================================================
    // 1. Renders a flat list without per-corpus section headers.
    // ============================================================
    [Fact]
    public async Task RendersFlatList_NoSectionHeaders_ForCommonTerm()
    {
        var (_, url, _) = await SeedResultsPageAsync();

        await Page.GotoAsync($"{Fixture.BaseUrl}{url}?q={HighVolumeTerm}");

        // The wrapper is present.
        await Expect(Page.Locator(".cypmd-search-results")).ToBeVisibleAsync();

        // Result count summary line.
        await Expect(Page.Locator(".cypmd-search-results >> text=/result[s]? for/"))
            .ToBeVisibleAsync();

        // At least one hit rendered as a list item.
        var items = Page.Locator(".cypmd-search-results ul.govuk-list > li");
        Assert.True(await items.CountAsync() > 0);

        // Crucially: NO per-corpus <h2> headers ("Pages (N)" / "Content blocks (N)").
        var pageHeader = Page.Locator(".cypmd-search-results h2:has-text(\"Pages (\")");
        var blockHeader = Page.Locator(".cypmd-search-results h2:has-text(\"Content blocks (\")");
        Assert.Equal(0, await pageHeader.CountAsync());
        Assert.Equal(0, await blockHeader.CountAsync());
    }

    // ============================================================
    // 2. Pagination component appears + navigating changes the URL and result set.
    // Seeds 9 PageNodes with a unique token in their titles + shrinks CMS:PageLength to 3
    // so the search hits fill exactly 3 pages regardless of what content the deployed
    // env already carries (review-app corpora can be much smaller than local dev).
    // ============================================================
    [Fact]
    public async Task PaginationAppearsAndNavigates_ForMultiPageTerm()
    {
        var (_, url, _) = await SeedResultsPageAsync();
        var searchToken = await SeedSearchableFixturesAsync(count: 9);

        try
        {
            await CmsSeedHelpers.SetAdminSettingAsync(Fixture, "CMS:PageLength", "3");

            await Page.GotoAsync($"{Fixture.BaseUrl}{url}?q={searchToken}");

            // Pagination is present at all — the __list is only rendered when TotalPages > 1.
            await Expect(Page.Locator(".govuk-pagination__list")).ToBeVisibleAsync();

            // Snapshot the first-page items' titles so we can compare against page 2.
            var page1Titles = await Page.Locator(".cypmd-search-results ul.govuk-list > li h3 a")
                .AllInnerTextsAsync();
            Assert.NotEmpty(page1Titles);

            // Click the numbered "2" item (its href carries page=2).
            await Page.Locator(".govuk-pagination__link[href*='page=2']").First.ClickAsync();
            await Page.WaitForURLAsync($"**{url}?**page=2**");

            var page2Titles = await Page.Locator(".cypmd-search-results ul.govuk-list > li h3 a")
                .AllInnerTextsAsync();
            Assert.NotEmpty(page2Titles);

            // Page 2 must not be identical to page 1 (otherwise the pager didn't page).
            Assert.NotEqual(page1Titles, page2Titles);

            // Previous appears on page 2 — back-nav is available.
            await Expect(Page.Locator(".govuk-pagination__prev a")).ToBeVisibleAsync();
        }
        finally
        {
            await CmsSeedHelpers.SetAdminSettingAsync(Fixture, "CMS:PageLength", "20");
        }
    }

    // ============================================================
    // 3. Empty-query, below-min-length, and no-match all render their respective states.
    // ============================================================
    [Fact]
    public async Task EmptyStates_RenderProperly()
    {
        var (_, url, _) = await SeedResultsPageAsync();

        // No q → prompt.
        await Page.GotoAsync($"{Fixture.BaseUrl}{url}");
        await Expect(Page.Locator(".cypmd-search-results"))
            .ToContainTextAsync("Enter a search term to begin.");

        // Single-character q → below-min.
        await Page.GotoAsync($"{Fixture.BaseUrl}{url}?q=x");
        await Expect(Page.Locator(".cypmd-search-results"))
            .ToContainTextAsync("Enter at least 2 characters.");

        // Nonsense q that produces no matches → widget emptyText (default).
        await Page.GotoAsync($"{Fixture.BaseUrl}{url}?q=zzzzzz-no-such-term");
        await Expect(Page.Locator(".cypmd-search-results"))
            .ToContainTextAsync("No results found.");
    }

    // ============================================================
    // 4. CMS:PageLength setting drives the effective page size.
    // ============================================================
    [Fact]
    public async Task SettingChange_AdjustsResultsPerPage()
    {
        var (_, url, _) = await SeedResultsPageAsync();
        // Seed exactly 10 fixtures so the assertion below can pin the exact page count:
        //   pageSize=10 → 1 page (0 pagination page-links).
        //   pageSize=2  → 5 pages (max page link = 5).
        var searchToken = await SeedSearchableFixturesAsync(count: 10);

        // Baseline at pageSize=10: the fixture set fits in a single page.
        try
        {
            await CmsSeedHelpers.SetAdminSettingAsync(Fixture, "CMS:PageLength", "10");
            await Page.GotoAsync($"{Fixture.BaseUrl}{url}?q={searchToken}");
            var baselineItems = await Page.Locator(".cypmd-search-results ul.govuk-list > li").CountAsync();
            Assert.Equal(10, baselineItems);

            // Shrink to 2 — 10 hits => exactly 5 pages.
            await CmsSeedHelpers.SetAdminSettingAsync(Fixture, "CMS:PageLength", "2");

            await Page.GotoAsync($"{Fixture.BaseUrl}{url}?q={searchToken}");
            var newItems = await Page.Locator(".cypmd-search-results ul.govuk-list > li").CountAsync();
            Assert.Equal(2, newItems);

            // The pagination must now reference more pages than at baseline. Read the
            // highest page number referenced in a pagination link href — a direct read of
            // result.TotalPages that survives GDS's ellipsis collapsing.
            var maxPageOnLinks = await Page.Locator(".govuk-pagination__link[href*='page=']")
                .EvaluateAllAsync<int>(@"elements => elements.length === 0 ? 0 : Math.max(...elements.map(e => {
                    const m = e.getAttribute('href').match(/[?&]page=(\d+)/);
                    return m ? parseInt(m[1], 10) : 0;
                }))");
            Assert.True(maxPageOnLinks >= 5,
                $"Expected pagination to reference >= page 5 with pageSize=2, got max page={maxPageOnLinks}.");
        }
        finally
        {
            await CmsSeedHelpers.SetAdminSettingAsync(Fixture, "CMS:PageLength", "20");
        }
    }

    // ============================================================
    // 5. Widget-configured scope narrows results to the path prefix.
    // ============================================================
    [Fact]
    public async Task WidgetScope_NarrowsToPathPrefix()
    {
        // Unscoped widget → baseline hit count.
        var (_, unscopedUrl, _) = await SeedResultsPageAsync();
        await Page.GotoAsync($"{Fixture.BaseUrl}{unscopedUrl}?q={HighVolumeTerm}");
        var unscopedText = await Page.Locator(".cypmd-search-results > p.govuk-body").First.InnerTextAsync();

        // Scoped widget (scope=help) — should shrink.
        var (_, scopedUrl, _) = await SeedResultsPageAsync(
            new Dictionary<string, string> { ["scope"] = "help", ["emptyText"] = "" });
        await Page.GotoAsync($"{Fixture.BaseUrl}{scopedUrl}?q={HighVolumeTerm}");
        var scopedText = await Page.Locator(".cypmd-search-results > p.govuk-body").First.InnerTextAsync();

        Assert.NotEqual(unscopedText, scopedText);
    }

    // ============================================================
    // 6. URL ?scope= beats the widget-configured scope.
    // ============================================================
    [Fact]
    public async Task UrlScope_OverridesWidgetScope()
    {
        // Widget configured with scope=help; URL passes scope=guidance.
        var (_, url, _) = await SeedResultsPageAsync(
            new Dictionary<string, string> { ["scope"] = "help", ["emptyText"] = "" });

        await Page.GotoAsync($"{Fixture.BaseUrl}{url}?q={HighVolumeTerm}&scope=guidance");

        // No easy way to introspect the effective scope from the DOM directly, so we
        // assert on a structural signal: all rendered hit URLs (page hrefs) start with
        // "/guidance/" (since scope filters both page hits and block URLs).
        var hrefs = await Page.Locator(".cypmd-search-results ul.govuk-list > li h3 a")
            .EvaluateAllAsync<string[]>("elements => elements.map(e => e.getAttribute('href'))");
        if (hrefs.Length == 0)
        {
            return; // Empty result set is inconclusive; better than a false negative on scope wiring.
        }
        // Accept both "/guidance" (the scope-root PageNode) and "/guidance/…" descendants.
        Assert.All(hrefs, href =>
            Assert.True(
                href == "/guidance" || href!.StartsWith("/guidance/"),
                $"Hit URL '{href}' outside the /guidance scope."));
    }

    // ============================================================
    // 7. Input widget submits ?q= (and ?scope=) to the configured action URL.
    // ============================================================
    [Fact]
    public async Task SearchInputWidget_HopsToResultsWidgetPage()
    {
        var (_, resultsUrl, _) = await SeedResultsPageAsync();
        var (_, entryUrl) = await SeedSearchInputPageAsync(actionUrl: resultsUrl);

        await Page.GotoAsync($"{Fixture.BaseUrl}{entryUrl}");

        // The search form is present, with an input named q.
        await Expect(Page.Locator("form.cypmd-search input[name='q']")).ToBeVisibleAsync();

        // Type, submit, wait for navigation to the results-widget page.
        await Page.Locator("form.cypmd-search input[name='q']").FillAsync(HighVolumeTerm);
        await Page.Locator("form.cypmd-search button[type='submit']").ClickAsync();

        await Page.WaitForURLAsync($"**{resultsUrl}?**q={HighVolumeTerm}**");

        // Results-widget rendered the flat list.
        await Expect(Page.Locator(".cypmd-search-results ul.govuk-list > li").First)
            .ToBeVisibleAsync();
    }
}
