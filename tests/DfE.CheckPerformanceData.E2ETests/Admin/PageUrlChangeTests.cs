using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;

namespace DfE.CheckPerformanceData.E2ETests.Admin;

// A change to a page's URL goes through a confirmation dialog, which posts its own form. That form
// must send every field of the page details form. If it does not, the save loses the fields that
// it leaves out: it cleared the page's search keywords.
[Trait("Category", "FullRegression")]
[Collection("E2E")]
public sealed class PageUrlChangeTests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    private Guid? _pageId;

    public override async Task DisposeAsync()
    {
        if (_pageId is { } id) await CmsSeedHelpers.TryDeletePageAsync(Fixture.SeedClient, id);
        await base.DisposeAsync();
    }

    private async Task<string> OpenNewPageDetailsAsync()
    {
        var segment = $"e2e-url-change-{Guid.NewGuid():N}"[..30];
        _pageId = await CmsSeedHelpers.CreatePageNodeAsync(
            Fixture.SeedClient, FixtureContent.RootId, "content", segment, "E2E URL change page");
        await Page.GotoAsync($"{Fixture.BaseUrl}/admin/pages/{_pageId}/edit#properties");
        return segment;
    }

    private ILocator Details => Page.Locator("#cpb-rename-form");

    // The save redirects back to the editor. Wait for that page, not for the old one.
    private Task ReloadedAfterAsync(Func<Task> action) => Page.RunAndWaitForResponseAsync(action,
        response => response.Request.Method == "GET" && response.Url.Contains($"/admin/pages/{_pageId}/edit"));

    private Task SaveDetailsAsync() =>
        ReloadedAfterAsync(() => Details.GetByRole(AriaRole.Button, new() { Name = "Save details" }).ClickAsync());

    private async Task ConfirmUrlChangeAsync()
    {
        await Details.GetByRole(AriaRole.Button, new() { Name = "Save details" }).ClickAsync();
        var dialog = Page.Locator("#cpb-rename-confirm");
        await Assertions.Expect(dialog).ToBeVisibleAsync();
        await ReloadedAfterAsync(() => dialog.GetByRole(AriaRole.Button, new() { Name = "Yes, change URL" }).ClickAsync());
        await Page.WaitForLoadStateAsync();
    }

    [Fact]
    public async Task ChangingOnlyTheUrl_KeepsTheSavedSearchKeywords()
    {
        var segment = await OpenNewPageDetailsAsync();
        await Details.Locator("#cpb-keywords").FillAsync("helpdesk phone");
        await SaveDetailsAsync();
        await Page.WaitForLoadStateAsync();

        await Details.Locator("input[name=segment]").FillAsync(segment + "-moved");
        await ConfirmUrlChangeAsync();

        await Assertions.Expect(Details.Locator("input[name=segment]")).ToHaveValueAsync(segment + "-moved");
        await Assertions.Expect(Details.Locator("#cpb-keywords")).ToHaveValueAsync("helpdesk phone");
    }

    [Fact]
    public async Task ChangingTheUrlWithOtherDetails_SavesThemAll()
    {
        var segment = await OpenNewPageDetailsAsync();
        await Details.Locator("input[name=segment]").FillAsync(segment + "-moved");
        await Details.Locator("#cpb-keywords").FillAsync("census return");
        await Details.Locator("#cpb-showinmenu").SetCheckedAsync(false);
        await Details.Locator("#cpb-appearinsearch").SetCheckedAsync(false);
        await ConfirmUrlChangeAsync();

        await Assertions.Expect(Details.Locator("input[name=segment]")).ToHaveValueAsync(segment + "-moved");
        await Assertions.Expect(Details.Locator("#cpb-keywords")).ToHaveValueAsync("census return");
        await Assertions.Expect(Details.Locator("#cpb-showinmenu")).Not.ToBeCheckedAsync();
        await Assertions.Expect(Details.Locator("#cpb-appearinsearch")).Not.ToBeCheckedAsync();
    }
}
