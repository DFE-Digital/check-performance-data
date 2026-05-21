using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;

namespace DfE.CheckPerformanceData.E2ETests.Web;

[Collection("E2E")]
[Trait("Category", "W4")]
public sealed class HardDeleteCancelTests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    private int _pageId;

    protected override async Task SeedAsync()
    {
        _pageId = await SeedHelpers.SeedWikiPageAsync(
            Fixture.SeedClient,
            title: "hard-delete-cancel",
            body: "Body of page used for cancel-path assertions.",
            parentId: null,
            TrackedIds);

        await SeedHelpers.SoftDeleteWikiPageAsync(Fixture.SeedClient, _pageId);
    }

    private string ModalSelector => $"#confirm-hard-delete-{_pageId}";
    private string TriggerSelector => $"[data-confirm-trigger='confirm-hard-delete-{_pageId}']";

    private async Task OpenModalAsync()
    {
        await Page.GotoAsync($"{Fixture.BaseUrl}/help/deleted");
        await Page.Locator(TriggerSelector).First.ClickAsync();
        await Expect(Page.Locator(ModalSelector)).ToHaveAttributeAsync("open", "");
    }

    // --- Modal_Cancel_Leaves_Page_Intact ---

    [Fact]
    public async Task Modal_Cancel_Leaves_Page_Intact()
    {
        await OpenModalAsync();

        await Page.Locator($"{ModalSelector} [data-confirm-cancel]").ClickAsync();
        await Expect(Page.Locator(ModalSelector)).Not.ToHaveAttributeAsync("open", "");

        // Reload and confirm the page row is still present.
        await Page.GotoAsync($"{Fixture.BaseUrl}/help/deleted");
        await Expect(Page.Locator(TriggerSelector).First).ToBeVisibleAsync();
    }

    // --- Modal_Backdrop_Click_Leaves_Page_Intact ---

    [Fact]
    public async Task Modal_Backdrop_Click_Leaves_Page_Intact()
    {
        await OpenModalAsync();

        var dialog = Page.Locator(ModalSelector);
        await dialog.EvaluateAsync(@"d => {
            d.dispatchEvent(new MouseEvent('click', { bubbles: true }));
        }");

        await Expect(dialog).Not.ToHaveAttributeAsync("open", "");

        await Page.GotoAsync($"{Fixture.BaseUrl}/help/deleted");
        await Expect(Page.Locator(TriggerSelector).First).ToBeVisibleAsync();
    }
}
