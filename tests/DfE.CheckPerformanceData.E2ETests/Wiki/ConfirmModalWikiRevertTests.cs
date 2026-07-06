using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;

namespace DfE.CheckPerformanceData.E2ETests.Wiki;

// Covers the govuk-confirm-modal usage on Views/Help/Versions.cshtml — the Revert
// button on the wiki page version-history table. Sister suite to ConfirmModalTests
// (which covers the _WikiTree.cshtml delete consumer).
[Collection("E2E")]
[Trait("Category", "ConfirmModal")]
public sealed class ConfirmModalWikiRevertTests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    private const string OriginalBody = "Original page body.";
    private const string EditedBody = "Edited page body.";

    private int _pageId;
    private int _version1Id;

    protected override async Task SeedAsync()
    {
        var (id, slug) = await SeedHelpers.SeedWikiPageReturningSlugAsync(
            Fixture.SeedClient,
            title: "revert-target",
            body: OriginalBody,
            parentId: null,
            TrackedIds);
        _pageId = id;

        await SeedHelpers.EditWikiPageAsync(
            Fixture.SeedClient,
            id,
            title: $"e2e-{Guid.NewGuid():N} revert-target-v2",
            body: EditedBody);

        _version1Id = await ResolveOriginalVersionIdAsync(slug);
    }

    [Fact]
    public async Task RevertButton_OpensModal_WithExpectedCopy()
    {
        await Page.GotoAsync($"{Fixture.BaseUrl}/help/versions/{_pageId}");

        var trigger = Page.Locator($"[data-confirm-trigger='confirm-revert-wiki-{_version1Id}']");
        await trigger.ClickAsync();

        var dialog = Page.Locator($"#confirm-revert-wiki-{_version1Id}");
        await Expect(dialog).ToHaveAttributeAsync("open", "");

        await Expect(dialog).ToContainTextAsync("Are you sure you want to revert to version 1?");
        await Expect(dialog).ToContainTextAsync("This will replace the current published content.");
        await Expect(dialog).ToContainTextAsync("Yes, revert");
    }

    [Fact]
    public async Task CancelLink_ClosesModal_NoRevert()
    {
        await Page.GotoAsync($"{Fixture.BaseUrl}/help/versions/{_pageId}");
        await Page.Locator($"[data-confirm-trigger='confirm-revert-wiki-{_version1Id}']").ClickAsync();

        var dialog = Page.Locator($"#confirm-revert-wiki-{_version1Id}");
        await Expect(dialog).ToHaveAttributeAsync("open", "");

        await Page.Locator($"#confirm-revert-wiki-{_version1Id} [data-confirm-cancel]").ClickAsync();
        await Expect(dialog).Not.ToHaveAttributeAsync("open", "");

        Assert.Contains($"/help/versions/{_pageId}", Page.Url);
    }

    // ConfirmRevert_PostsForm_RestoresOriginalContent removed: after the revert POST the
    // controller redirects to /help/{slug}, and that reader route was retired along with
    // HelpController.Index. The remaining modal tests in this file exercise the versions
    // page and the confirm-modal UX itself, both of which still render — the value of the
    // revert-then-render assertion is now covered by unit + integration tests against
    // WikiService.RevertToVersionAsync directly.

    [Fact]
    public async Task EscKey_ClosesModal_RestoresFocusToTrigger()
    {
        await Page.GotoAsync($"{Fixture.BaseUrl}/help/versions/{_pageId}");

        var trigger = Page.Locator($"[data-confirm-trigger='confirm-revert-wiki-{_version1Id}']");
        await trigger.ClickAsync();

        await Page.Keyboard.PressAsync("Escape");
        await Expect(Page.Locator($"#confirm-revert-wiki-{_version1Id}")).Not.ToHaveAttributeAsync("open", "");

        var triggerHasFocus = await Page.EvaluateAsync<bool>(
            $"() => document.activeElement && document.activeElement.matches(\"[data-confirm-trigger='confirm-revert-wiki-{_version1Id}']\")");
        Assert.True(triggerHasFocus);
    }

    [Fact]
    public async Task CurrentVersion_DoesNotRenderModalTrigger()
    {
        await Page.GotoAsync($"{Fixture.BaseUrl}/help/versions/{_pageId}");

        var currentTag = Page.Locator(".govuk-tag", new() { HasTextString = "Current" });
        await Expect(currentTag).ToBeVisibleAsync();

        var triggersOnPage = await Page.Locator("[data-confirm-trigger^='confirm-revert-wiki-']").CountAsync();
        Assert.Equal(1, triggersOnPage);
    }

    // Scrape the Versions page HTML for the first non-current version id. Bound to the
    // markup of Views/Help/Versions.cshtml (the data-version-id attribute on each row).
    // Returns the id used to build the modal id confirm-revert-wiki-{id}.
    private async Task<int> ResolveOriginalVersionIdAsync(string slug)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri(Fixture.SeedClient.BaseAddress!, $"/help/versions/{_pageId}"));
        using var response = await TestHttpClients.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        var matches = System.Text.RegularExpressions.Regex.Matches(
            html,
            "data-version-id=\"(?<id>\\d+)\"",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (matches.Count < 2)
        {
            throw new InvalidOperationException(
                $"Expected 2 versions on /help/versions/{_pageId} for slug '{slug}'; found {matches.Count}.");
        }

        // Versions are rendered newest-first (current is row 0). Row 1 is the original.
        return int.Parse(matches[1].Groups["id"].Value);
    }
}
