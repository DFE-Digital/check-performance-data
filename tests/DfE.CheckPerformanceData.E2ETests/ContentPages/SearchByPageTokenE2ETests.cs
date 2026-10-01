using System.Net;
using System.Text.RegularExpressions;
using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;

namespace DfE.CheckPerformanceData.E2ETests.ContentPages;

// A Search or Search results widget limited to chosen pages stores them by page id and searches
// them by short token (/search?pages=t1,t2). These tests seed a small group of pages, each
// titled with a fresh word only they contain, so a search for that word is answered entirely by
// the seeded pages:
//
//   group-a            ticked
//     group-a/child    beneath a ticked page, so included
//   group-b            ticked
//   outside            not ticked, so never in a result
[Collection("E2E")]
public sealed class SearchByPageTokenE2ETests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    private static readonly Regex Token = new("^[0-9A-Za-z]{8}$");

    private readonly List<Guid> _createdPages = [];

    public override async Task DisposeAsync()
    {
        // Children before parents: a page with children cannot be deleted.
        foreach (var id in Enumerable.Reverse(_createdPages))
        {
            await CmsSeedHelpers.TryDeletePageAsync(Fixture.SeedClient, id);
        }
        await base.DisposeAsync();
    }

    private sealed record Group(string Word, Guid A, Guid B, string APath, string ChildPath, string BPath, string OutsidePath);

    private async Task<Guid> CreatePublishedPageAsync(Guid parent, string segment, string title)
    {
        var id = await CmsSeedHelpers.CreatePageNodeAsync(Fixture.SeedClient, parent, "content", segment, title);
        _createdPages.Add(id);
        await CmsSeedHelpers.PublishDraftAsync(Fixture.SeedClient, id);
        return id;
    }

    private async Task<Group> SeedGroupAsync()
    {
        var word = "cypdtok" + Guid.NewGuid().ToString("N")[..10];
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var a = await CreatePublishedPageAsync(FixtureContent.RootId, $"e2e-tok-a-{suffix}", $"Group A {word}");
        await CreatePublishedPageAsync(a, "child", $"Group A child {word}");
        var b = await CreatePublishedPageAsync(FixtureContent.RootId, $"e2e-tok-b-{suffix}", $"Group B {word}");
        await CreatePublishedPageAsync(FixtureContent.RootId, $"e2e-tok-out-{suffix}", $"Outside {word}");

        var root = FixtureContent.RootPath;
        return new Group(word, a, b,
            $"{root}/e2e-tok-a-{suffix}", $"{root}/e2e-tok-a-{suffix}/child", $"{root}/e2e-tok-b-{suffix}",
            $"{root}/e2e-tok-out-{suffix}");
    }

    private async Task<string> SeedWidgetPageAsync(string widgetType, Dictionary<string, string> props)
    {
        var segment = $"e2e-tok-widget-{Guid.NewGuid():N}";
        var id = await CmsSeedHelpers.CreatePageNodeAsync(
            Fixture.SeedClient, FixtureContent.RootId, "content", segment, "E2E page token widget");
        _createdPages.Add(id);
        await CmsSeedHelpers.AddWidgetAsync(Fixture.SeedClient, id, "0.0", widgetType);
        await CmsSeedHelpers.UpdateWidgetAsync(Fixture.SeedClient, id, "0.0", widgetType, props);
        await CmsSeedHelpers.PublishDraftAsync(Fixture.SeedClient, id);
        return $"{FixtureContent.RootPath}/{segment}";
    }

    private static Dictionary<string, string> SearchProps(Group g, bool instant = false)
    {
        var props = new Dictionary<string, string>
        {
            ["label"] = "Search the group",
            ["action"] = "/search",
            ["placeholder"] = "",
            ["buttonText"] = "Search",
            ["searchIn"] = "path",
            ["scope"] = "",
            ["scopePageIds"] = $"{g.A},{g.B}",
            ["noResultsText"] = "Nothing found",
        };
        if (instant) props["instant"] = "true";
        return props;
    }

    private async Task RenameAsync(Guid id, string segment, string title)
    {
        var (token, cookie) = await AntiforgeryHelpers.ScrapeAsync(Fixture.SeedClient, "/dev/antiforgery-token");
        using var req = new HttpRequestMessage(HttpMethod.Post, new Uri(Fixture.SeedClient.BaseAddress!, $"/admin/pages/{id}/rename"))
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["segment"] = segment,
                ["title"] = title,
                ["appearInSearch"] = "true",
                ["__RequestVerificationToken"] = token,
            }),
        };
        req.Headers.Add("Cookie", cookie);
        var response = await TestHttpClients.SendAsync(req);
        Assert.True(response.StatusCode is HttpStatusCode.Found or HttpStatusCode.Redirect,
            $"Rename returned {(int)response.StatusCode}");
    }

    private async Task<string[]> ResultHrefsAsync(string selector) =>
        (await Page.Locator(selector).EvaluateAllAsync<string[]>("els => els.map(e => e.getAttribute('href'))"))
            .OrderBy(h => h, StringComparer.Ordinal).ToArray();

    // ============================================================
    // 1. The widget's form names its pages by two 8-character tokens, and submitting it
    //    searches those pages and everything beneath them only.
    // ============================================================
    [Fact]
    public async Task SearchWidget_SubmitsPageTokens_AndFindsOnlyThePickedPagesAndBeneath()
    {
        var g = await SeedGroupAsync();
        var url = await SeedWidgetPageAsync("search", SearchProps(g));

        await Page.GotoAsync($"{Fixture.BaseUrl}{url}");
        var form = Page.Locator("form.cypmd-search");
        Assert.Equal(0, await form.Locator("input[name='scope']").CountAsync());
        var pages = await form.Locator("input[name='pages']").GetAttributeAsync("value");
        var tokens = pages!.Split(',');
        Assert.Equal(2, tokens.Length);
        Assert.All(tokens, t => Assert.Matches(Token, t));

        await form.Locator("input[name='q']").FillAsync(g.Word);
        await form.Locator("button[type='submit']").ClickAsync();
        await Page.WaitForURLAsync(new Regex("/search\\?"));

        Assert.Contains($"pages={Uri.EscapeDataString(pages)}", Page.Url);
        Assert.DoesNotContain("scope=", Page.Url);
        var hrefs = await ResultHrefsAsync($"a[href^='{FixtureContent.RootPath}/e2e-tok-']");
        Assert.Equal(new[] { g.APath, g.ChildPath, g.BPath }.OrderBy(h => h, StringComparer.Ordinal), hrefs);
    }

    // ============================================================
    // 2. A picked page that is renamed is still searched: the token follows the page.
    // ============================================================
    [Fact]
    public async Task SearchWidget_StillSearchesAPickedPage_AfterItIsRenamed()
    {
        var g = await SeedGroupAsync();
        var url = await SeedWidgetPageAsync("search", SearchProps(g));
        var renamedSegment = $"e2e-tok-renamed-{Guid.NewGuid():N}"[..28];
        await RenameAsync(g.B, renamedSegment, $"Group B renamed {g.Word}");

        await Page.GotoAsync($"{Fixture.BaseUrl}{url}");
        var form = Page.Locator("form.cypmd-search");
        await form.Locator("input[name='q']").FillAsync(g.Word);
        await form.Locator("button[type='submit']").ClickAsync();
        await Page.WaitForURLAsync(new Regex("/search\\?"));

        var hrefs = await ResultHrefsAsync($"a[href^='{FixtureContent.RootPath}/e2e-tok-']");
        Assert.Contains($"{FixtureContent.RootPath}/{renamedSegment}", hrefs);
        Assert.Contains(g.APath, hrefs);
        Assert.DoesNotContain(g.OutsidePath, hrefs);
        Assert.DoesNotContain(g.BPath, hrefs);
    }

    // ============================================================
    // 3. The results page names the searched pages by their readable paths: in the count line
    //    and in the scope comment after the heading. Never the tokens.
    // ============================================================
    [Fact]
    public async Task SearchByTokens_NamesTheReadablePaths_InTheCountLineAndTheScopeComment()
    {
        var g = await SeedGroupAsync();
        var url = await SeedWidgetPageAsync("search", SearchProps(g));
        await Page.GotoAsync($"{Fixture.BaseUrl}{url}");
        var pages = await Page.Locator("form.cypmd-search input[name='pages']").GetAttributeAsync("value");

        var response = await Page.GotoAsync($"{Fixture.BaseUrl}/search?q={g.Word}&pages={Uri.EscapeDataString(pages!)}");
        var html = await response!.TextAsync();

        var a = g.APath.TrimStart('/');
        var b = g.BPath.TrimStart('/');
        Assert.Matches(new Regex($@"</h1>\s*<!-- search scope: {Regex.Escape(a)},{Regex.Escape(b)} -->"), html);
        await Expect(Page.Locator("main")).ToContainTextAsync($"for {g.Word} in {g.APath}, {g.BPath}");
        Assert.DoesNotContain($"search scope: {pages}", html);
    }

    // ============================================================
    // 4. An old-style ?scope= link still works exactly as before.
    // ============================================================
    [Fact]
    public async Task OldStyleScopeLink_StillSearchesThatPathAndBeneath()
    {
        var g = await SeedGroupAsync();

        await Page.GotoAsync($"{Fixture.BaseUrl}/search?q={g.Word}&scope={Uri.EscapeDataString(g.APath)}");

        var hrefs = await ResultHrefsAsync($"a[href^='{FixtureContent.RootPath}/e2e-tok-']");
        Assert.Equal(new[] { g.APath, g.ChildPath }.OrderBy(h => h, StringComparer.Ordinal), hrefs);
    }

    // ============================================================
    // 5. A widget saved with the old path scope still searches its pages.
    // ============================================================
    [Fact]
    public async Task WidgetWithOnlyAnOldPathScope_StillSearchesThosePages()
    {
        var g = await SeedGroupAsync();
        var props = SearchProps(g);
        props["scopePageIds"] = "";
        props["scope"] = $"{g.APath},{g.BPath}";
        var url = await SeedWidgetPageAsync("search", props);

        await Page.GotoAsync($"{Fixture.BaseUrl}{url}");
        var form = Page.Locator("form.cypmd-search");
        Assert.Equal(0, await form.Locator("input[name='pages']").CountAsync());
        await form.Locator("input[name='q']").FillAsync(g.Word);
        await form.Locator("button[type='submit']").ClickAsync();
        await Page.WaitForURLAsync(new Regex("/search\\?"));

        Assert.Contains("scope=", Page.Url);
        var hrefs = await ResultHrefsAsync($"a[href^='{FixtureContent.RootPath}/e2e-tok-']");
        Assert.Equal(new[] { g.APath, g.ChildPath, g.BPath }.OrderBy(h => h, StringComparer.Ordinal), hrefs);
    }

    // ============================================================
    // 6. A results widget limited by page id shows results from those pages only.
    // ============================================================
    [Fact]
    public async Task ResultsWidget_LimitedByPageIds_ShowsOnlyThosePagesAndBeneath()
    {
        var g = await SeedGroupAsync();
        var url = await SeedWidgetPageAsync("results", new Dictionary<string, string>
        {
            ["scope"] = "",
            ["scopePageIds"] = $"{g.A},{g.B}",
            ["emptyText"] = "",
        });

        await Page.GotoAsync($"{Fixture.BaseUrl}{url}?q={g.Word}");

        var hrefs = await ResultHrefsAsync(".cypmd-search-results ul.govuk-list > li h3 a");
        Assert.Equal(new[] { g.APath, g.ChildPath, g.BPath }.OrderBy(h => h, StringComparer.Ordinal), hrefs);
    }

    // ============================================================
    // 7. Instant search on a widget limited by page id suggests only those pages and beneath.
    // ============================================================
    [Fact]
    public async Task InstantSearch_LimitedByPageIds_SuggestsOnlyThosePages()
    {
        var g = await SeedGroupAsync();
        var url = await SeedWidgetPageAsync("search", SearchProps(g, instant: true));

        await Page.GotoAsync($"{Fixture.BaseUrl}{url}");
        var input = Page.Locator("input.autocomplete__input");
        await input.ClickAsync();
        await input.FillAsync(g.Word);

        var options = Page.Locator("li.autocomplete__option");
        await Expect(options).ToHaveCountAsync(3);
        var labels = await options.AllInnerTextsAsync();
        Assert.DoesNotContain(labels, l => l.Contains("Outside"));
    }
}
