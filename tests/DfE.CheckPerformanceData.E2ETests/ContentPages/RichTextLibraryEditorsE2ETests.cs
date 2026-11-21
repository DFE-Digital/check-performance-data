using System.Text.RegularExpressions;
using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;

namespace DfE.CheckPerformanceData.E2ETests.ContentPages;

// The wiki editor and both content block editors use the media library, as the page editor does:
// the image dialog's browse button opens the media picker, the chosen image is saved as a library
// image, the page that shows the content writes signed copies, and the image page lists where the
// image is used.
//
// Images uploaded here have unique e2e-{guid} names and are deleted again. The accessibility
// statement block gets its own value back. A seeded e2e block is left behind, as other content
// block tests do (there is no delete route for blocks).
[Trait("Category", "FullRegression")]
[Collection("E2E")]
public sealed class RichTextLibraryEditorsE2ETests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    private const string AccessibilityKey = "accessibility";
    private readonly List<Guid> _pages = [];
    private readonly List<Guid> _images = [];
    private readonly string _term = $"e2e-{Guid.NewGuid():N}";
    private string? _accessibilityValue;

    public override async Task DisposeAsync()
    {
        try
        {
            await AuthHelpers.ImpersonateAsAdminAsync(Fixture);
            if (_accessibilityValue is not null)
                await SeedHelpers.EditContentBlockAsync(Fixture.SeedClient, AccessibilityKey, _accessibilityValue);
            for (var i = _pages.Count - 1; i >= 0; i--)
                await CmsSeedHelpers.TryDeletePageAsync(Fixture.SeedClient, _pages[i]);
            foreach (var id in _images) await MediaSeedHelpers.ClearUsagesAsync(Fixture.SeedClient, id);
            foreach (var id in _images) await MediaSeedHelpers.DeleteAsync(Fixture.SeedClient, id);
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(Fixture);
            await base.DisposeAsync();
        }
    }

    private ILocator Picker => Page.Locator("#media-picker");
    private ILocator ImageDialog => Page.Locator(".tox-dialog");

    private async Task SignInAsAdminAsync()
    {
        var adminCookie = await AuthHelpers.ImpersonateAsAdminAsync(Fixture);
        if (!string.IsNullOrEmpty(adminCookie) && adminCookie.IndexOf('=') is var eq and > 0)
            await Context.AddCookiesAsync([new Cookie { Name = adminCookie[..eq], Value = adminCookie[(eq + 1)..], Url = Fixture.BaseUrl }]);
    }

    private async Task<Guid> UploadUniquePngAsync()
    {
        var id = await MediaSeedHelpers.UploadAsync(Fixture.SeedClient, $"{_term}-{Guid.NewGuid():N}.png", MediaFixtures.UniquePng(1200, 600));
        _images.Add(id);
        return id;
    }

    private async Task WaitForEditorAsync(ILocator editor)
    {
        await Expect(editor.Locator(".tox-tinymce")).ToBeVisibleAsync();
        await Expect(editor.FrameLocator("iframe.tox-edit-area__iframe").Locator("body")).ToBeAttachedAsync();
    }

    // Opens TinyMCE's image dialog (the toolbar may hide the button behind "more"), browses to the
    // media picker, chooses the image and saves the dialog with the given alt text.
    private async Task InsertLibraryImageAsync(ILocator editor, Guid mediaId, string alt)
    {
        var image = editor.Locator("button[aria-label='Insert/edit image']");
        var more = editor.Locator("button[aria-label='Reveal or hide additional toolbar items']");
        if (await image.CountAsync() == 0 || !await image.First.IsVisibleAsync()) await more.ClickAsync();
        await Page.Locator("button[aria-label='Insert/edit image']").First.ClickAsync();
        await Expect(ImageDialog).ToBeVisibleAsync();

        await ImageDialog.Locator(".tox-browse-url").ClickAsync();
        await Expect(Picker).ToBeVisibleAsync();
        await Picker.Locator("#media-picker-search").FillAsync(_term);
        await Picker.GetByRole(AriaRole.Button, new() { Name = "Search", Exact = true }).ClickAsync();
        await Expect(Picker.Locator("[data-media-picker-status]")).ToContainTextAsync($"‘{_term}’");
        await Picker.Locator($"label[for='media-picker-item-{mediaId}']").ClickAsync();
        await Picker.GetByRole(AriaRole.Button, new() { Name = "Use this image" }).ClickAsync();
        await Expect(Picker).Not.ToBeVisibleAsync();

        await Expect(ImageDialog.GetByLabel("Source")).ToHaveValueAsync(new Regex($"^/media/{mediaId:D}/1/[^?]+\\.png$"));
        await ImageDialog.GetByLabel("Alternative description").FillAsync(alt);
        await ImageDialog.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await Expect(editor.FrameLocator("iframe.tox-edit-area__iframe").Locator($"img[src^='/media/{mediaId:D}/']")).ToBeVisibleAsync();
    }

    private async Task ExpectSignedLibraryImageAsync(ILocator img, Guid mediaId)
    {
        await Expect(img).ToHaveAttributeAsync("src", new Regex($"^/media/{mediaId:D}/1/[^?]+\\.webp\\?w=\\d+&sig="));
        await Expect(img).ToHaveAttributeAsync("srcset", new Regex("sig="));
    }

    [Fact]
    public async Task TheContentBlocksPageEditor_InsertsALibraryImage_AndTheImagePageListsTheBlock()
    {
        await SignInAsAdminAsync();
        var mediaId = await UploadUniquePngAsync();
        var key = await SeedHelpers.SeedContentBlockAsync(Fixture.SeedClient, "library-image", "<p>Block text</p>");

        await Page.GotoAsync($"{Fixture.BaseUrl}/admin/content-blocks?edit={key}#edit-{key}");
        var editor = Page.Locator($"[id='edit-{key}']");
        await WaitForEditorAsync(editor);
        await InsertLibraryImageAsync(editor, mediaId, "A block chart");
        await editor.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

        // The saved value names the library image; the version history shows signed copies.
        await Page.GotoAsync($"{Fixture.BaseUrl}/admin/content-blocks?edit={key}#edit-{key}");
        var saved = await Page.Locator($"[id='cb-{key}']").InputValueAsync();
        Assert.Contains($"data-media-id=\"{mediaId:D}\"", saved);
        Assert.DoesNotContain("data:image", saved);
        await Page.GotoAsync($"{Fixture.BaseUrl}/content-block/versions/{key}");
        await Page.Locator("tr.version-row").First.ClickAsync();
        await ExpectSignedLibraryImageAsync(Page.Locator($".version-preview-panel:not([hidden]) img[data-media-id='{mediaId:D}']"), mediaId);

        await Page.GotoAsync($"{Fixture.BaseUrl}/admin/media/{mediaId}");
        await Expect(Page.Locator("[data-media-usage-blocks] a", new() { HasText = key }))
            .ToHaveAttributeAsync("href", $"/admin/content-blocks?edit={key}#edit-{key}");
        await Expect(Page.Locator("[data-media-usage-none]")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task TheBlockEditorOnAPage_InsertsALibraryImage_AndThePageShowsIt()
    {
        await SignInAsAdminAsync();
        var mediaId = await UploadUniquePngAsync();

        await Page.GotoAsync($"{Fixture.BaseUrl}/accessibility?edit={AccessibilityKey}");
        _accessibilityValue = await Page.Locator($"#cb-{AccessibilityKey}").InputValueAsync();
        var editor = Page.Locator($"#block-{AccessibilityKey}");
        await WaitForEditorAsync(editor);
        await InsertLibraryImageAsync(editor, mediaId, $"{_term} statement image");
        await editor.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

        await Page.GotoAsync($"{Fixture.BaseUrl}/accessibility");
        await ExpectSignedLibraryImageAsync(Page.Locator($"#block-{AccessibilityKey} img[data-media-id='{mediaId:D}']"), mediaId);

        await Page.GotoAsync($"{Fixture.BaseUrl}/admin/media/{mediaId}");
        await Expect(Page.Locator("[data-media-usage-blocks] a", new() { HasText = AccessibilityKey })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task TheWikiEditor_InsertsALibraryImage_AndThePublishedPageShowsIt()
    {
        await SignInAsAdminAsync();
        var mediaId = await UploadUniquePngAsync();
        var segment = $"e2e-wiki-library-{Guid.NewGuid():N}";
        var title = $"E2E wiki library image {_term}";
        var pageId = await CmsSeedHelpers.CreatePageNodeAsync(Fixture.SeedClient, FixtureContent.RootId, "wiki", segment, title);
        _pages.Add(pageId);

        await Page.GotoAsync($"{Fixture.BaseUrl}/admin/pages/{pageId}/edit");
        var editor = Page.Locator("form:has(#page-content)");
        await WaitForEditorAsync(editor);
        await InsertLibraryImageAsync(editor, mediaId, "A wiki chart");
        await editor.GetByRole(AriaRole.Button, new() { Name = "Publish", Exact = true }).ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

        await Page.GotoAsync($"{Fixture.BaseUrl}{FixtureContent.RootPath}/{segment}");
        await ExpectSignedLibraryImageAsync(Page.Locator($"main img[data-media-id='{mediaId:D}']"), mediaId);

        await Page.GotoAsync($"{Fixture.BaseUrl}/admin/media/{mediaId}");
        await Expect(Page.Locator("[data-media-usage-current] a", new() { HasText = title }))
            .ToHaveAttributeAsync("href", $"/admin/pages/{pageId}");
    }
}
