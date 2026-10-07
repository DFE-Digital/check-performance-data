using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;

namespace DfE.CheckPerformanceData.E2ETests.ContentPages;

// The page picker in the Search widget editor lists every CMS page. cms-page-scope-filter.js adds
// a filter box, a "show only selected" box and a live count. These tests seed three pages with
// words only they contain, plus a page whose Search widget is scoped to the first, and drive the
// picker in the real editor.
[Collection("E2E")]
public sealed class PageScopePickerFilterE2ETests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    private readonly List<Guid> _createdPages = [];

    public override async Task DisposeAsync()
    {
        foreach (var id in Enumerable.Reverse(_createdPages))
        {
            await CmsSeedHelpers.TryDeletePageAsync(Fixture.SeedClient, id);
        }
        await base.DisposeAsync();
    }

    private sealed record Seeded(Guid WidgetPageId, string WordA, string WordB, string WordC, string SegmentB);

    private async Task<Guid> CreatePublishedPageAsync(string segment, string title)
    {
        var id = await CmsSeedHelpers.CreatePageNodeAsync(Fixture.SeedClient, FixtureContent.RootId, "content", segment, title);
        _createdPages.Add(id);
        await CmsSeedHelpers.PublishDraftAsync(Fixture.SeedClient, id);
        return id;
    }

    private async Task<Seeded> SeedAsync()
    {
        var n = Guid.NewGuid().ToString("N");
        var wordA = "zebrafa" + n[..8];
        var wordB = "yakfb" + n[8..16];
        var wordC = "xerusfc" + n[16..24];
        var segB = $"e2e-scope-b-{n[..8]}";

        var a = await CreatePublishedPageAsync($"e2e-scope-a-{n[..8]}", $"Scope {wordA}");
        await CreatePublishedPageAsync(segB, $"Scope {wordB}");
        await CreatePublishedPageAsync($"e2e-scope-c-{n[..8]}", $"Scope {wordC}");

        var widgetPage = await CmsSeedHelpers.CreatePageNodeAsync(
            Fixture.SeedClient, FixtureContent.RootId, "content", $"e2e-scope-widget-{n}", "E2E scope picker widget");
        _createdPages.Add(widgetPage);
        await CmsSeedHelpers.AddWidgetAsync(Fixture.SeedClient, widgetPage, "0.0", "search");
        await CmsSeedHelpers.UpdateWidgetAsync(Fixture.SeedClient, widgetPage, "0.0", "search", new Dictionary<string, string>
        {
            ["label"] = "Search the group",
            ["action"] = "/search",
            ["placeholder"] = "",
            ["buttonText"] = "Search",
            ["searchIn"] = "path",
            ["scope"] = "",
            ["scopePageIds"] = a.ToString(),
            ["noResultsText"] = "Nothing found",
        });
        return new Seeded(widgetPage, wordA, wordB, wordC, segB);
    }

    private ILocator Picker => Page.Locator("[data-cpb-scope-picker]").First;
    private ILocator Filter => Picker.Locator("[data-cpb-scope-filter] input[type=search]");
    private ILocator Status => Picker.Locator("[data-cpb-scope-status]");
    private ILocator VisibleItems => Picker.Locator(".cpb-scope-picker__item:not([hidden])");

    private ILocator BoxFor(string word) =>
        Picker.Locator(".cpb-scope-picker__item", new() { HasText = word }).Locator("input[name=scopePages]");

    private async Task OpenEditorAsync(Seeded s)
    {
        await Page.GotoAsync($"{Fixture.BaseUrl}/admin/pages/{s.WidgetPageId}/edit");
        await Picker.Locator("xpath=ancestor::details[1]/summary").First.ClickAsync();
        await Filter.WaitForAsync();
    }

    [Fact]
    public async Task TypingInTheFilter_HidesPagesThatDoNotMatch_ByTitleOrPath()
    {
        var s = await SeedAsync();
        await OpenEditorAsync(s);

        await Filter.FillAsync(s.WordB);
        Assert.Equal(1, await VisibleItems.CountAsync());
        Assert.Contains(s.WordB, await VisibleItems.First.InnerTextAsync());

        // The path segment is matched too, not just the title.
        await Filter.FillAsync(s.SegmentB);
        Assert.Equal(1, await VisibleItems.CountAsync());
        Assert.Contains(s.WordB, await VisibleItems.First.InnerTextAsync());

        await Filter.FillAsync("");
        Assert.True(await VisibleItems.CountAsync() >= 4);
    }

    [Fact]
    public async Task Filter_AnnouncesHowManyPagesMatch_AndHowManyAreSelected()
    {
        var s = await SeedAsync();
        await OpenEditorAsync(s);

        var total = await Picker.Locator(".cpb-scope-picker__item").CountAsync();
        Assert.Equal($"Showing {total} of {total} pages. 1 selected.", (await Status.InnerTextAsync()).Trim());

        await Filter.FillAsync(s.WordB);
        Assert.Equal($"Showing 1 of {total} pages. 1 selected.", (await Status.InnerTextAsync()).Trim());

        await BoxFor(s.WordB).CheckAsync();
        Assert.Equal($"Showing 1 of {total} pages. 2 selected.", (await Status.InnerTextAsync()).Trim());
    }

    [Fact]
    public async Task ATickedPageHiddenByTheFilter_IsStillSavedWithTheWidget()
    {
        var s = await SeedAsync();
        await OpenEditorAsync(s);

        await Filter.FillAsync(s.WordB);
        Assert.False(await BoxFor(s.WordA).IsVisibleAsync());
        await BoxFor(s.WordB).CheckAsync();

        await Picker.Locator("xpath=ancestor::form[1]").Locator("button[type=submit]").First.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await OpenEditorAsync(s);
        Assert.True(await BoxFor(s.WordA).IsCheckedAsync());
        Assert.True(await BoxFor(s.WordB).IsCheckedAsync());
        Assert.False(await BoxFor(s.WordC).IsCheckedAsync());
    }

    [Fact]
    public async Task ShowOnlySelected_ListsJustTheTickedPages()
    {
        var s = await SeedAsync();
        await OpenEditorAsync(s);
        await BoxFor(s.WordC).CheckAsync();

        await Picker.Locator("[data-cpb-scope-selected-only]").CheckAsync();

        Assert.Equal(2, await VisibleItems.CountAsync());
        Assert.True(await BoxFor(s.WordA).IsVisibleAsync());
        Assert.True(await BoxFor(s.WordC).IsVisibleAsync());
        Assert.False(await BoxFor(s.WordB).IsVisibleAsync());
    }

    [Fact]
    public async Task PressingEnterInTheFilter_DoesNotSubmitTheWidgetForm()
    {
        var s = await SeedAsync();
        await OpenEditorAsync(s);

        var navigated = false;
        Page.FrameNavigated += (_, _) => navigated = true;
        await Filter.FillAsync(s.WordB);
        await Filter.PressAsync("Enter");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        Assert.False(navigated);
        Assert.Contains("/edit", Page.Url);
        Assert.Equal(1, await VisibleItems.CountAsync());
    }

    [Fact]
    public async Task NoMatches_ShowsTheEmptyMessage()
    {
        var s = await SeedAsync();
        await OpenEditorAsync(s);
        var empty = Picker.Locator("[data-cpb-scope-empty]");
        Assert.False(await empty.IsVisibleAsync());

        await Filter.FillAsync("nothing-has-this-" + Guid.NewGuid().ToString("N"));

        Assert.Equal(0, await VisibleItems.CountAsync());
        Assert.True(await empty.IsVisibleAsync());
        Assert.Equal("No pages match your filter.", (await empty.InnerTextAsync()).Trim());
    }
}
