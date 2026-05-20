using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;

namespace DfE.CheckPerformanceData.E2ETests.Wiki;

[Collection("E2E")]
[Trait("Category", "ConfirmModal")]
public sealed class ConfirmModalTests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    private string _slug = "";
    private int _pageId;

    protected override async Task SeedAsync()
    {
        var (id, slug) = await SeedHelpers.SeedWikiPageReturningSlugAsync(
            Fixture.SeedClient,
            title: "modal-target",
            body: "Page body for modal test.",
            parentId: null,
            TrackedIds);

        _pageId = id;
        _slug = slug;
    }

    // --- Open ---

    [Fact]
    public async Task TriggerClick_OpensDialog_FocusOnCancel()
    {
        await Page.GotoAsync($"{Fixture.BaseUrl}/help/{_slug}?edit");

        var trigger = Page.Locator($"[data-confirm-trigger='confirm-delete-{_pageId}']");
        await trigger.ClickAsync();

        var dialog = Page.Locator($"#confirm-delete-{_pageId}");
        await Expect(dialog).ToHaveAttributeAsync("open", "");

        var isModal = await dialog.EvaluateAsync<bool>("d => d.matches(':modal')");
        Assert.True(isModal);

        var activeMatchesCancel = await Page.EvaluateAsync<bool>(
            "() => document.activeElement && document.activeElement.matches('[data-confirm-cancel]')");
        Assert.True(activeMatchesCancel,
            "autofocus on Cancel button should be honoured by .showModal()");
    }

    // --- Cancel paths ---

    [Fact]
    public async Task EscKey_ClosesDialog_RestoresFocus()
    {
        await OpenModalAsync();

        await Page.Keyboard.PressAsync("Escape");
        await Expect(Page.Locator($"#confirm-delete-{_pageId}")).Not.ToHaveAttributeAsync("open", "");

        var triggerHasFocus = await Page.EvaluateAsync<bool>(
            $"() => document.activeElement && document.activeElement.matches(\"[data-confirm-trigger='confirm-delete-{_pageId}']\")");
        Assert.True(triggerHasFocus, "focus must return to the trigger button after Esc");
    }

    [Fact]
    public async Task BackdropClick_ClosesDialog_NoNavigation()
    {
        await OpenModalAsync();
        var initialUrl = Page.Url;

        var dialog = Page.Locator($"#confirm-delete-{_pageId}");
        // The backdrop is the dialog's ::backdrop pseudo. Clicking it bubbles a
        // click to the dialog with e.target === dialog. We synthesise that click
        // by dispatching directly on the dialog element (real backdrop clicks are
        // outside Playwright's box-model click target since the pseudo can't be
        // selected).
        await dialog.EvaluateAsync(@"d => {
            d.dispatchEvent(new MouseEvent('click', { bubbles: true }));
        }");

        await Expect(dialog).Not.ToHaveAttributeAsync("open", "");
        Assert.Equal(initialUrl, Page.Url);
    }

    [Fact]
    public async Task CancelLinkClick_ClosesDialog_NoNavigation()
    {
        await OpenModalAsync();
        var initialUrl = Page.Url;

        await Page.Locator($"#confirm-delete-{_pageId} [data-confirm-cancel]").ClickAsync();
        await Expect(Page.Locator($"#confirm-delete-{_pageId}")).Not.ToHaveAttributeAsync("open", "");
        Assert.Equal(initialUrl, Page.Url);
    }

    // --- Confirm path ---

    [Fact]
    public async Task ConfirmClick_PostsForm_DeletesPage()
    {
        await OpenModalAsync();

        await Task.WhenAll(
            Page.WaitForURLAsync(url => !url.Contains(_slug)),
            Page.Locator($"#confirm-delete-{_pageId} button[type=submit]").ClickAsync()
        );
    }

    // --- Focus trap ---

    [Fact]
    public async Task TabCycle_StaysInsideDialog()
    {
        // DOM focus order with the modal-dialogue structure:
        //   1. X close button (govuk-modal-dialogue__close)
        //   2. Yes, delete (submit)
        //   3. Cancel link  ← autofocused on open
        //
        // From Cancel: Tab forward wraps to X close (first); the next Tab
        // lands on Yes; the cycle stays inside the dialog throughout.
        await OpenModalAsync();

        await Page.Keyboard.PressAsync("Tab");
        var afterFirstTab = await Page.EvaluateAsync<string>(
            "() => document.activeElement ? document.activeElement.getAttribute('aria-label') || document.activeElement.textContent.trim() : ''");
        Assert.Equal("close", afterFirstTab);

        await Page.Keyboard.PressAsync("Tab");
        var afterSecondTab = await Page.EvaluateAsync<string>(
            "() => document.activeElement ? document.activeElement.textContent.trim() : ''");
        Assert.Contains("Yes, delete", afterSecondTab);

        await Page.Keyboard.PressAsync("Tab");
        var afterThirdTab = await Page.EvaluateAsync<string>(
            "() => document.activeElement ? document.activeElement.textContent.trim() : ''");
        Assert.Equal("Cancel", afterThirdTab);
    }

    // --- Computed-CSS regression pins ---

    [Fact]
    public async Task DestructiveVariant_WarningIconHasGdsStyling()
    {
        await OpenModalAsync();

        var icon = Page.Locator($"#confirm-delete-{_pageId} .govuk-warning-text__icon");
        await Expect(icon).ToHaveTextAsync("!");

        Assert.Equal("50%",
            await icon.EvaluateAsync<string>("el => getComputedStyle(el).borderRadius"));
        Assert.Equal("rgb(11, 12, 12)",
            await icon.EvaluateAsync<string>("el => getComputedStyle(el).backgroundColor"));
        Assert.Equal("rgb(255, 255, 255)",
            await icon.EvaluateAsync<string>("el => getComputedStyle(el).color"));
    }

    [Fact]
    public async Task ModalChrome_HasGdsColours()
    {
        await OpenModalAsync();

        // Backdrop is the dialog's ::backdrop pseudo at 80% black opacity.
        var backdropBg = await Page.EvaluateAsync<string>($@"
            () => {{
                var d = document.getElementById('confirm-delete-{_pageId}');
                return getComputedStyle(d, '::backdrop').backgroundColor;
            }}");
        Assert.Equal("rgba(11, 12, 12, 0.8)", backdropBg);

        // Header band is GDS black with white text.
        var headerBg = await Page.EvaluateAsync<string>($@"
            () => {{
                var h = document.querySelector('#confirm-delete-{_pageId} .govuk-modal-dialogue__header');
                return getComputedStyle(h).backgroundColor;
            }}");
        Assert.Equal("rgb(11, 12, 12)", headerBg);

        // Pinned to the actual computed value of govuk-button--warning shipped by
        // GovUk.Frontend.AspNetCore 4.1.0 (govuk-frontend 6.1.0): #ca3535.
        var confirmBtn = Page.Locator($"#confirm-delete-{_pageId} .govuk-button--warning");
        Assert.Equal("rgb(202, 53, 53)",
            await confirmBtn.EvaluateAsync<string>("el => getComputedStyle(el).backgroundColor"));
    }

    // --- helpers ---

    private async Task OpenModalAsync()
    {
        await Page.GotoAsync($"{Fixture.BaseUrl}/help/{_slug}?edit");
        await Page.Locator($"[data-confirm-trigger='confirm-delete-{_pageId}']").ClickAsync();
        await Expect(Page.Locator($"#confirm-delete-{_pageId}")).ToHaveAttributeAsync("open", "");
    }
}
