using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;

namespace DfE.CheckPerformanceData.E2ETests.ContentPages;

// Choosing which heading levels the page-nav widget lists. The case worth proving in a browser
// is the one an author actually hits: a page that skips a level, where ticking H2 and H4 has to
// nest the H4s under the H2s rather than dropping them or flattening them.
//
// Pairs with ContentNavBuilderTests (the nesting rules), NavLevelSelectionTests (props to level
// set) and WidgetEditorContractTests (the form fields).
[Collection("E2E")]
public sealed class PageNavLevelsE2ETests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    private readonly List<Guid> _createdPages = [];

    public override async Task DisposeAsync()
    {
        for (var i = _createdPages.Count - 1; i >= 0; i--)
            await CmsSeedHelpers.TryDeletePageAsync(Fixture.SeedClient, _createdPages[i]);
        await base.DisposeAsync();
    }

    // A page whose headings run H2, H3, H4 so every combination has something to show.
    private async Task<(Guid Id, string Url)> SeedAsync(Dictionary<string, string>? navProps = null)
    {
        var segment = $"e2e-navlevels-{Guid.NewGuid():N}";
        var id = await CmsSeedHelpers.CreatePageNodeAsync(
            Fixture.SeedClient, CmsSeedHelpers.HelpRootId, "content", segment, "E2E nav levels");
        _createdPages.Add(id);

        await CmsSeedHelpers.AddWidgetAsync(Fixture.SeedClient, id, "0.0", "pagenav");
        if (navProps is not null)
            await CmsSeedHelpers.UpdateWidgetAsync(Fixture.SeedClient, id, "0.0", "pagenav", navProps);

        var levels = new[] { 2, 3, 4, 2 };
        var names = new[] { "Section one", "Sub of one", "Detail of one", "Section two" };
        for (var i = 0; i < levels.Length; i++)
        {
            var path = $"0.{i + 1}";
            await CmsSeedHelpers.AddWidgetAsync(Fixture.SeedClient, id, path, "heading");
            await CmsSeedHelpers.UpdateWidgetAsync(Fixture.SeedClient, id, path, "heading",
                new Dictionary<string, string> { ["level"] = levels[i].ToString(), ["text"] = names[i] });
        }
        await CmsSeedHelpers.PublishDraftAsync(Fixture.SeedClient, id);

        return (id, $"/help/{segment}");
    }

    private static Dictionary<string, string> Nav(params int[] ticked)
    {
        var props = new Dictionary<string, string>
        {
            ["mode"] = "headings",
            ["childrenParentPath"] = "",
            ["searchPath"] = "",
            ["searchLabel"] = "Search",
        };
        // Every level is posted explicitly, the way the editor form does it — the props builder
        // keeps the registry default for a key it never receives, so a partial post would leave
        // a level that defaults to on still on.
        for (var level = 1; level <= 6; level++)
            props[$"h{level}"] = ticked.Contains(level) ? "true" : "false";
        return props;
    }

    private async Task<IReadOnlyList<string>> NavTextsAsync() =>
        await Page.Locator("nav.moj-side-navigation a").AllInnerTextsAsync();

    [Fact]
    public async Task ByDefault_TheNavShowsH2AndH3Only()
    {
        var (_, url) = await SeedAsync();

        await Page.GotoAsync($"{Fixture.BaseUrl}{url}");

        var texts = await NavTextsAsync();
        Assert.Equal(["Section one", "Sub of one", "Section two"], texts);
    }

    [Fact]
    public async Task TickingH2AndH4_NestsTheH4UnderTheH2_AndDropsTheH3()
    {
        var (_, url) = await SeedAsync(Nav(2, 4));

        await Page.GotoAsync($"{Fixture.BaseUrl}{url}");

        var texts = await NavTextsAsync();
        Assert.Equal(["Section one", "Detail of one", "Section two"], texts);

        // And it is nested, not flattened: the H4 sits inside the first H2's own list.
        var nestedUnderFirst = await Page
            .Locator("nav.moj-side-navigation > ul > li:first-child ul a")
            .AllInnerTextsAsync();
        Assert.Equal(["Detail of one"], nestedUnderFirst);
    }

    [Fact]
    public async Task TickingOneLevel_GivesAFlatList()
    {
        var (_, url) = await SeedAsync(Nav(2));

        await Page.GotoAsync($"{Fixture.BaseUrl}{url}");

        Assert.Equal(["Section one", "Section two"], await NavTextsAsync());
        Assert.Equal(0, await Page.Locator("nav.moj-side-navigation ul ul").CountAsync());
    }

    [Fact]
    public async Task TickingAllThree_NestsThreeDeep()
    {
        var (_, url) = await SeedAsync(Nav(2, 3, 4));

        await Page.GotoAsync($"{Fixture.BaseUrl}{url}");

        Assert.Equal(
            ["Section one", "Sub of one", "Detail of one", "Section two"],
            await NavTextsAsync());
        Assert.True(await Page.Locator("nav.moj-side-navigation ul ul ul a").CountAsync() > 0);
    }

    [Fact]
    public async Task UntickingALevelInTheEditor_TurnsItOff()
    {
        // The whole round trip, through the real form. An unticked box posts nothing, and the
        // props builder keeps the registry default for a field it never sees — so a level that
        // defaults to on stays on unless the form also posts an explicit "false" for it. This
        // is the test that holds that companion field in place.
        var (id, url) = await SeedAsync();

        await Page.GotoAsync($"{Fixture.BaseUrl}/admin/pages/{id}/edit");
        await Page.Locator("summary:has-text(\"Edit page navigation\")").First.ClickAsync();

        await Page.Locator("input[type=checkbox][name='props[h3]']").First.UncheckAsync();
        await Page.Locator("input[type=checkbox][name='props[h4]']").First.CheckAsync();
        await Page.Locator("[data-cpb-pagenav-headings]")
            .Locator("xpath=ancestor::form")
            .Locator("button[type=submit]").First.ClickAsync();
        await Page.WaitForLoadStateAsync(Microsoft.Playwright.LoadState.NetworkIdle);

        await CmsSeedHelpers.PublishDraftAsync(Fixture.SeedClient, id);
        await Page.GotoAsync($"{Fixture.BaseUrl}{url}");

        Assert.Equal(["Section one", "Detail of one", "Section two"], await NavTextsAsync());
    }

    [Fact]
    public async Task TheEditorOffersABoxForEveryLevel_WithH2AndH3TickedOnANewWidget()
    {
        var (id, _) = await SeedAsync();

        await Page.GotoAsync($"{Fixture.BaseUrl}/admin/pages/{id}/edit");
        await Page.Locator("summary:has-text(\"Edit page navigation\")").First.ClickAsync();

        var boxes = Page.Locator("input[type=checkbox][name^='props[h']");
        Assert.Equal(6, await boxes.CountAsync());

        var ticked = await Page.Locator("input[type=checkbox][name^='props[h']:checked")
            .EvaluateAllAsync<string[]>("els => els.map(e => e.name)");
        Assert.Equal(["props[h2]", "props[h3]"], ticked);
    }
}
