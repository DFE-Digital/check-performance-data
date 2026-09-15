using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;

namespace DfE.CheckPerformanceData.E2ETests.Admin;

// The single-page-search section of the search-analytics dashboard: a real on-page search is
// made as a visitor, then read back through the admin surface an editor would actually use.
//
// Pairs with OnPageSearchQueryTests (the SQL behind it) and SearchSurfaceFilterTests (the
// surface filter). This one proves the route, the gate and the two view states hang together.
[Collection("E2E")]
public sealed class OnPageSearchSectionTests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    private readonly List<Guid> _createdPages = [];

    public override async Task DisposeAsync()
    {
        for (var i = _createdPages.Count - 1; i >= 0; i--)
        {
            await CmsSeedHelpers.TryDeletePageAsync(Fixture.SeedClient, _createdPages[i]);
        }
        await base.DisposeAsync();
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

    [Fact]
    public async Task AnOnPageSearch_AppearsInTheSinglePageSection_AndDrillsInToItsTerms()
    {
        var term = "cypdsec" + Guid.NewGuid().ToString("N")[..10].ToLowerInvariant();
        var segment = $"e2e-section-{Guid.NewGuid():N}";

        var id = await CmsSeedHelpers.CreatePageNodeAsync(
            Fixture.SeedClient, CmsSeedHelpers.HelpRootId, "content", segment, "E2E single-page section");
        _createdPages.Add(id);

        await CmsSeedHelpers.AddWidgetAsync(Fixture.SeedClient, id, "0.0", "search");
        await CmsSeedHelpers.UpdateWidgetAsync(Fixture.SeedClient, id, "0.0", "search",
            new Dictionary<string, string>
            {
                ["label"] = "Search this page",
                ["action"] = "/search",
                ["buttonText"] = "Search",
                ["searchIn"] = "page",
                ["instant"] = "true",
                ["noResultsText"] = "No matches",
            });
        await CmsSeedHelpers.AddWidgetAsync(Fixture.SeedClient, id, "0.1", "heading");
        await CmsSeedHelpers.UpdateWidgetAsync(Fixture.SeedClient, id, "0.1", "heading",
            new Dictionary<string, string> { ["level"] = "2", ["text"] = "Providing evidence" });
        await CmsSeedHelpers.PublishDraftAsync(Fixture.SeedClient, id);

        var hostPath = $"/help/{segment}";

        // Search as a visitor would, and settle the query by leaving the box.
        await Page.GotoAsync($"{Fixture.BaseUrl}{hostPath}");
        var input = Page.Locator("input.autocomplete__input");
        await input.ClickAsync();
        await input.FillAsync(term);
        await Expect(Page.Locator(".autocomplete__menu")).ToContainTextAsync("No matches");
        await Page.Keyboard.PressAsync("Tab");

        try
        {
            var adminCookie = await AuthHelpers.ImpersonateAsAdminAsync(Fixture);
            AttachCookieToContext(adminCookie);

            // The terms drill-in is addressed by path, so this does not depend on where the
            // page happens to land in a paged list that every other test is also writing to.
            var drillIn = $"{Fixture.BaseUrl}/admin/Search/OnPage?path={Uri.EscapeDataString(hostPath)}&range=24h";

            // The sink drains on a timer.
            for (var attempt = 0; attempt < 30; attempt++)
            {
                await Page.GotoAsync(drillIn);
                if ((await Page.Locator("body").InnerTextAsync()).Contains(term, StringComparison.Ordinal)) break;
                await Page.WaitForTimeoutAsync(500);
            }

            await Expect(Page.Locator("h1")).ToContainTextAsync("Single-page search");
            await Expect(Page.Locator("#sa-onpage-table")).ToContainTextAsync(term);

            // And the list view renders pages with rows in it, each linking to its own drill-in.
            await Page.GotoAsync($"{Fixture.BaseUrl}/admin/Search/OnPage?range=24h");
            await Expect(Page.Locator("#sa-onpage-table")).ToBeVisibleAsync();
            Assert.True(await Page.Locator("#sa-onpage-table tbody tr").CountAsync() > 0);
            Assert.True(await Page.Locator("#sa-onpage-table tbody a[href*='OnPage?path=']").CountAsync() > 0);
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(Fixture);
        }
    }

    [Fact]
    public async Task TheSurfaceFilter_RoundTripsOnTheDashboard()
    {
        try
        {
            var adminCookie = await AuthHelpers.ImpersonateAsAdminAsync(Fixture);
            AttachCookieToContext(adminCookie);

            await Page.GotoAsync($"{Fixture.BaseUrl}/admin/Search/?range=24h");
            // Unfiltered means every surface counted, so every box is ticked.
            Assert.Equal(3, await Page.Locator("input[name='surface']:checked").CountAsync());

            await Page.GotoAsync($"{Fixture.BaseUrl}/admin/Search/?range=24h&surface=instant-page");
            var checkedValues = await Page.Locator("input[name='surface']:checked")
                .EvaluateAllAsync<string[]>("els => els.map(e => e.value)");

            Assert.Equal(["instant-page"], checkedValues);
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(Fixture);
        }
    }
}
