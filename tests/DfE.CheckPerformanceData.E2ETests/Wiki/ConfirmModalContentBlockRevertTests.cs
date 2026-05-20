using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;

namespace DfE.CheckPerformanceData.E2ETests.Wiki;

// Covers the govuk-confirm-modal usage on Views/ContentBlock/Versions.cshtml — the
// Revert button on the content-block version-history table. Sister suite to
// ConfirmModalWikiRevertTests.
[Collection("E2E")]
[Trait("Category", "ConfirmModal")]
public sealed class ConfirmModalContentBlockRevertTests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    private const string OriginalValue = "Original block value.";
    private const string EditedValue = "Edited block value.";

    private string _key = "";
    private int _version1Id;

    protected override async Task SeedAsync()
    {
        _key = await SeedHelpers.SeedContentBlockAsync(
            Fixture.SeedClient,
            keyPrefix: "modal-revert",
            value: OriginalValue);

        await SeedHelpers.EditContentBlockAsync(Fixture.SeedClient, _key, EditedValue);

        _version1Id = await ResolveOriginalVersionIdAsync();
    }

    [Fact]
    public async Task RevertButton_OpensModal_WithExpectedCopy()
    {
        await Page.GotoAsync($"{Fixture.BaseUrl}/content-block/versions/{_key}");

        await Page.Locator($"[data-confirm-trigger='confirm-revert-cb-{_version1Id}']").ClickAsync();

        var dialog = Page.Locator($"#confirm-revert-cb-{_version1Id}");
        await Expect(dialog).ToHaveAttributeAsync("open", "");

        await Expect(dialog).ToContainTextAsync("Are you sure you want to revert to version 1?");
        await Expect(dialog).ToContainTextAsync("This will replace the current published content.");
        await Expect(dialog).ToContainTextAsync("Yes, revert");
    }

    [Fact]
    public async Task CancelLink_ClosesModal_NoRevert()
    {
        await Page.GotoAsync($"{Fixture.BaseUrl}/content-block/versions/{_key}");
        await Page.Locator($"[data-confirm-trigger='confirm-revert-cb-{_version1Id}']").ClickAsync();

        var dialog = Page.Locator($"#confirm-revert-cb-{_version1Id}");
        await Expect(dialog).ToHaveAttributeAsync("open", "");

        await Page.Locator($"#confirm-revert-cb-{_version1Id} [data-confirm-cancel]").ClickAsync();
        await Expect(dialog).Not.ToHaveAttributeAsync("open", "");

        Assert.Contains($"/content-block/versions/{_key}", Page.Url);
    }

    [Fact]
    public async Task ConfirmRevert_PostsForm_RestoresOriginalValue()
    {
        await Page.GotoAsync($"{Fixture.BaseUrl}/content-block/versions/{_key}");
        await Page.Locator($"[data-confirm-trigger='confirm-revert-cb-{_version1Id}']").ClickAsync();

        await Expect(Page.Locator($"#confirm-revert-cb-{_version1Id}")).ToHaveAttributeAsync("open", "");

        await Task.WhenAll(
            Page.WaitForURLAsync(url => !url.Contains("/versions/")),
            Page.Locator($"#confirm-revert-cb-{_version1Id} button[type=submit]").ClickAsync()
        );

        await Page.GotoAsync($"{Fixture.BaseUrl}/content-block/versions/{_key}");
        await Expect(Page.Locator("#main-content")).ToContainTextAsync(OriginalValue);
    }

    [Fact]
    public async Task CurrentVersion_DoesNotRenderModalTrigger()
    {
        await Page.GotoAsync($"{Fixture.BaseUrl}/content-block/versions/{_key}");

        var currentTag = Page.Locator(".govuk-tag", new() { HasTextString = "Current" });
        await Expect(currentTag).ToBeVisibleAsync();

        var triggersOnPage = await Page.Locator("[data-confirm-trigger^='confirm-revert-cb-']").CountAsync();
        Assert.Equal(1, triggersOnPage);
    }

    private async Task<int> ResolveOriginalVersionIdAsync()
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri(Fixture.SeedClient.BaseAddress!, $"/content-block/versions/{_key}"));
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
                $"Expected 2 versions on /content-block/versions/{_key}; found {matches.Count}.");
        }

        // Newest-first ordering: row 0 = current, row 1 = original v1.
        return int.Parse(matches[1].Groups["id"].Value);
    }
}
