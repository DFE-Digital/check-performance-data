using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;

namespace DfE.CheckPerformanceData.E2ETests.Wiki;

[Collection("E2E")]
[Trait("Category", "W2")]
public sealed class SearchSidebarBackLinkTests(PlaywrightFixture fixture) : PageTest, IAsyncLifetime
{
    private readonly PlaywrightFixture _fixture = fixture;
    private readonly List<int> _trackedIds = [];
    private string _keyword = "";

    public new async Task InitializeAsync()
    {
        await base.InitializeAsync();

        // Lowercase letters + 6-char hex slice keeps the token FTS-friendly across the
        // english tsearch config (no hyphenation, no digits-only stripping).
        _keyword = $"e2emark{Guid.NewGuid().ToString("N")[..6]}";

        await SeedHelpers.SeedWikiPageAsync(
            _fixture.SeedClient,
            title: $"E2E search target {_keyword}",
            body: $"This page mentions {_keyword} for the search smoke test.",
            parentId: null,
            _trackedIds);
    }

    public new async Task DisposeAsync()
    {
        foreach (var id in _trackedIds.AsEnumerable().Reverse())
        {
            await SeedHelpers.SoftDeleteWikiPageAsync(_fixture.SeedClient, id);
        }
        await base.DisposeAsync();
    }

    // --- BackLinkAndSidebarPresent ---

    [Fact]
    public async Task BackLinkAndSidebarPresent()
    {
        await Page.GotoAsync($"{_fixture.BaseUrl}/help/search?q={_keyword}");

        await Expect(Page.Locator("a.govuk-back-link[href='/help']")).ToBeVisibleAsync();

        var sidebar = Page.Locator("aside.wiki-sidebar");
        await Expect(sidebar).ToBeVisibleAsync();

        // _WikiSearch partial renders <input name="q"> inside a govuk-input search form.
        await Expect(sidebar.Locator("input[name='q']")).ToBeVisibleAsync();

        // _WikiTree partial renders <ul class="tv tv-root"> at the root level.
        await Expect(sidebar.Locator("ul.tv.tv-root")).ToBeVisibleAsync();
    }

    // --- MarkElementInResultSnippet ---

    [Fact]
    public async Task MarkElementInResultSnippet()
    {
        await Page.GotoAsync($"{_fixture.BaseUrl}/help/search?q={_keyword}");

        // Scope to the main results region so the assertion does not accidentally pick up <mark>
        // elements emitted by the chrome (sidebar / back-link area). Postgres ts_headline wraps
        // each match hit in <mark>; Search.cshtml renders the snippet via @Html.Raw, preserving
        // the wrapping for the seeded keyword.
        var marks = Page.Locator("main.wiki-main mark");
        var count = await marks.CountAsync();
        Assert.True(count >= 1, $"expected at least one <mark> in search results, found {count}");
    }
}
