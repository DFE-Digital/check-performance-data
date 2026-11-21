using System.Text.RegularExpressions;
using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;

namespace DfE.CheckPerformanceData.E2ETests.ContentPages;

// Media library images in the rich text editor, as the dev admin: an image chosen in TinyMCE's
// image dialog through the media picker, a pasted image that uploads to the library, and a
// replaced library image that the live page then shows. The page writes signed responsive copies.
//
// The shared samples are only chosen, never changed. Images uploaded here have unique e2e-{guid}
// names and are deleted again.
[Trait("Category", "FullRegression")]
[Collection("E2E")]
public sealed class RichTextMediaE2ETests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    private readonly List<Guid> _pages = [];
    private readonly List<Guid> _images = [];
    private readonly string _term = $"e2e-{Guid.NewGuid():N}";

    public override async Task DisposeAsync()
    {
        try
        {
            await AuthHelpers.ImpersonateAsAdminAsync(Fixture);
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

    private ILocator Widget => Page.Locator("[id='widget-0.0']");
    private ILocator Picker => Page.Locator("#media-picker");
    private ILocator ImageDialog => Page.Locator(".tox-dialog");
    private IFrameLocator EditorBody => Widget.FrameLocator("iframe.tox-edit-area__iframe");
    private ILocator Html => Widget.Locator("textarea.cpb-richtext");

    private static readonly Regex MediaIdInHtml =
        new("data-media-id=\"([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})\"", RegexOptions.Compiled);

    private async Task SignInAsAdminAsync()
    {
        var adminCookie = await AuthHelpers.ImpersonateAsAdminAsync(Fixture);
        if (!string.IsNullOrEmpty(adminCookie) && adminCookie.IndexOf('=') is var eq and > 0)
            await Context.AddCookiesAsync([new Cookie { Name = adminCookie[..eq], Value = adminCookie[(eq + 1)..], Url = Fixture.BaseUrl }]);
    }

    // A draft page with one rich-text widget, optionally with stored HTML. Returns its id and address.
    private async Task<(Guid Id, string Url)> NewRichTextPageAsync(string? html = null)
    {
        var segment = $"e2e-rich-text-media-{Guid.NewGuid():N}";
        var id = await CmsSeedHelpers.CreatePageNodeAsync(Fixture.SeedClient, FixtureContent.RootId, "content", segment, "E2E rich text media");
        _pages.Add(id);
        await CmsSeedHelpers.AddWidgetAsync(Fixture.SeedClient, id, "0.0", "richtext");
        if (html is not null)
            await CmsSeedHelpers.UpdateWidgetAsync(Fixture.SeedClient, id, "0.0", "richtext", [new("html", html)]);
        return (id, $"{FixtureContent.RootPath}/{segment}");
    }

    private async Task OpenEditorAsync(Guid pageId)
    {
        await Page.GotoAsync($"{Fixture.BaseUrl}/admin/pages/{pageId}/edit");
        await Widget.Locator("summary").First.ClickAsync();
        await Expect(Widget.Locator(".tox-tinymce")).ToBeVisibleAsync();
        await Expect(EditorBody.Locator("body")).ToBeAttachedAsync();
    }

    // The toolbar hides the buttons that do not fit behind a "more" button.
    private async Task OpenImageDialogAsync()
    {
        var image = Page.Locator("button[aria-label='Insert/edit image']");
        var more = Widget.Locator("button[aria-label='Reveal or hide additional toolbar items']");
        if (await image.CountAsync() == 0 || !await image.First.IsVisibleAsync()) await more.ClickAsync();
        await image.First.ClickAsync();
        await Expect(ImageDialog).ToBeVisibleAsync();
    }

    private async Task SaveAndPublishAsync(Guid pageId)
    {
        await Widget.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
        await Expect(Page.Locator(".govuk-error-summary")).ToHaveCountAsync(0);
        await CmsSeedHelpers.PublishDraftAsync(Fixture.SeedClient, pageId);
    }

    // The saved rich text, read back from a fresh load of the editor.
    private async Task<string> SavedHtmlAsync(Guid pageId)
    {
        await Page.GotoAsync($"{Fixture.BaseUrl}/admin/pages/{pageId}/edit");
        return await Html.InputValueAsync();
    }

    private async Task<Guid> UploadUniquePngAsync(int width = 48, int height = 24)
    {
        var id = await MediaSeedHelpers.UploadAsync(Fixture.SeedClient, $"{_term}-{Guid.NewGuid():N}.png", MediaFixtures.UniquePng(width, height));
        _images.Add(id);
        return id;
    }

    private async Task ChooseInPickerAsync(Guid mediaId, string search)
    {
        await Expect(Picker).ToBeVisibleAsync();
        await Picker.Locator("#media-picker-search").FillAsync(search);
        await Picker.GetByRole(AriaRole.Button, new() { Name = "Search", Exact = true }).ClickAsync();
        // Wait for the search result, so the choice is not made in the list the search replaces.
        await Expect(Picker.Locator("[data-media-picker-status]")).ToContainTextAsync($"‘{search}’");
        await Picker.Locator($"label[for='media-picker-item-{mediaId}']").ClickAsync();
        await Picker.GetByRole(AriaRole.Button, new() { Name = "Use this image" }).ClickAsync();
        await Expect(Picker).Not.ToBeVisibleAsync();
    }

    // Checks that each srcset copy is signed and loads as an image.
    private async Task AssertSignedCopiesLoadAsync(ILocator img)
    {
        var src = await img.GetAttributeAsync("src");
        Assert.Contains("sig=", src);
        var srcset = await img.GetAttributeAsync("srcset");
        Assert.False(string.IsNullOrEmpty(srcset));
        foreach (var entry in srcset!.Split(", "))
        {
            var url = entry[..entry.LastIndexOf(' ')];
            Assert.Contains("sig=", url);
            var response = await Page.APIRequest.GetAsync($"{Fixture.BaseUrl}{url}");
            Assert.Equal(200, response.Status);
            Assert.StartsWith("image/", response.Headers["content-type"]);
        }
    }

    [Fact]
    public async Task AnEditor_ChoosesAnImageFromTheLibrary_InTheImageDialog_AndThePageShowsSignedCopies()
    {
        await SignInAsAdminAsync();
        var mediaId = await UploadUniquePngAsync(1400, 700);
        var (pageId, url) = await NewRichTextPageAsync();
        await OpenEditorAsync(pageId);

        await OpenImageDialogAsync();
        await Expect(ImageDialog).ToBeVisibleAsync();
        await ImageDialog.Locator(".tox-browse-url").ClickAsync();
        await ChooseInPickerAsync(mediaId, _term);

        var source = ImageDialog.GetByLabel("Source");
        await Expect(source).ToHaveValueAsync(new Regex($"^/media/{mediaId:D}/1/[^?]+\\.png$"));
        await ImageDialog.GetByLabel("Alternative description").FillAsync("A wide test image");
        await ImageDialog.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await Expect(EditorBody.Locator($"img[src^='/media/{mediaId:D}/']")).ToBeVisibleAsync();

        await SaveAndPublishAsync(pageId);
        var saved = await SavedHtmlAsync(pageId);
        Assert.Contains($"data-media-id=\"{mediaId:D}\"", saved);
        Assert.DoesNotContain("sig=", saved);
        // The dialog takes the size from the library, so the page does not move as the image loads.
        Assert.Contains("width=\"1400\"", saved);
        Assert.Contains("height=\"700\"", saved);

        await Page.GotoAsync($"{Fixture.BaseUrl}{url}");
        var img = Page.Locator($"main .govuk-body img[data-media-id='{mediaId:D}']");
        await Expect(img).ToHaveAttributeAsync("alt", "A wide test image");
        await Expect(img).ToHaveAttributeAsync("loading", "lazy");
        await Expect(img).ToHaveAttributeAsync("src", new Regex($"^/media/{mediaId:D}/1/[^?]+\\.webp\\?w=\\d+&sig="));
        await AssertSignedCopiesLoadAsync(img);
    }

    [Fact]
    public async Task TheImageDialog_TakesTheLibraryAltText_WhenAnImageIsChosen()
    {
        var sample = await MediaSeedHelpers.SeedSampleAsync(Fixture.SeedClient, "svg", alt: "Green and blue blocks");
        await SignInAsAdminAsync();
        var (pageId, _) = await NewRichTextPageAsync();
        await OpenEditorAsync(pageId);

        await OpenImageDialogAsync();
        await ImageDialog.Locator(".tox-browse-url").ClickAsync();
        await ChooseInPickerAsync(sample.Id, "sample");

        await Expect(ImageDialog.GetByLabel("Alternative description")).ToHaveValueAsync("Green and blue blocks");
        await ImageDialog.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();
    }

    [Fact]
    public async Task APastedImage_UploadsToTheLibrary_AndIsSavedAsALibraryImage()
    {
        await SignInAsAdminAsync();
        var (pageId, _) = await NewRichTextPageAsync("<p>Paste here</p>");
        await OpenEditorAsync(pageId);

        // A real clipboard needs OS permissions, so the test sends the browser's own paste event
        // with the image file in its clipboard data, as a paste from another program would.
        var name = $"{_term}-pasted.png";
        var bytes = MediaFixtures.UniquePng(40, 20);
        await EditorBody.Locator("body p").ClickAsync();
        await EditorBody.Locator("body").EvaluateAsync(@"(body, args) => {
            const bytes = Uint8Array.from(atob(args.data), c => c.charCodeAt(0));
            const file = new File([bytes], args.name, { type: 'image/png' });
            const data = new DataTransfer();
            data.items.add(file);
            body.dispatchEvent(new ClipboardEvent('paste', { clipboardData: data, bubbles: true, cancelable: true }));
        }", new { data = Convert.ToBase64String(bytes), name });

        var pasted = EditorBody.Locator("img[src^='/media/']");
        await Expect(pasted).ToHaveCountAsync(1, new() { Timeout = 15000 });

        await SaveAndPublishAsync(pageId);
        var saved = await SavedHtmlAsync(pageId);
        var match = MediaIdInHtml.Match(saved);
        Assert.True(match.Success, $"No library image in the saved text: {saved[..Math.Min(300, saved.Length)]}");
        var mediaId = Guid.Parse(match.Groups[1].Value);
        _images.Add(mediaId);
        Assert.DoesNotContain("data:image", saved);
        Assert.DoesNotContain("blob:", saved);

        // The image is in the library.
        var library = await Page.APIRequest.GetAsync($"{Fixture.BaseUrl}/admin/media/{mediaId:D}");
        Assert.Equal(200, library.Status);
    }

    [Fact]
    public async Task AnUploadTheLibraryRefuses_ShowsTheServersMessage()
    {
        await SignInAsAdminAsync();
        var (pageId, _) = await NewRichTextPageAsync("<p>Paste here</p>");
        await OpenEditorAsync(pageId);

        // A file named .png that holds text: the upload pipeline checks the bytes and refuses it.
        await EditorBody.Locator("body p").ClickAsync();
        await EditorBody.Locator("body").EvaluateAsync(@"body => {
            const file = new File(['not an image'], 'fake.png', { type: 'image/png' });
            const data = new DataTransfer();
            data.items.add(file);
            body.dispatchEvent(new ClipboardEvent('paste', { clipboardData: data, bubbles: true, cancelable: true }));
        }");

        var notice = Page.Locator(".tox-notification--error");
        await Expect(notice).ToBeVisibleAsync(new() { Timeout = 15000 });
        // The library's own reason, not a general failure.
        await Expect(notice).Not.ToContainTextAsync("could not be uploaded");
        await Expect(notice).Not.ToBeEmptyAsync();
        await Expect(EditorBody.Locator("img")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task ReplacingALibraryImage_ChangesTheLiveRichTextPage()
    {
        await SignInAsAdminAsync();
        var mediaId = await UploadUniquePngAsync(800, 400);
        var (pageId, url) = await NewRichTextPageAsync(
            $"<p><img data-media-id=\"{mediaId:D}\" src=\"/media/{mediaId:D}/1/x.png\" alt=\"Before and after\" width=\"400\" height=\"200\"></p>");
        await CmsSeedHelpers.PublishDraftAsync(Fixture.SeedClient, pageId);

        await Page.GotoAsync($"{Fixture.BaseUrl}{url}");
        var img = Page.Locator($"main .govuk-body img[data-media-id='{mediaId:D}']");
        await Expect(img).ToHaveAttributeAsync("src", new Regex($"^/media/{mediaId:D}/1/"));

        // The new version is square, so the page keeps the width the editor set and follows its shape.
        await MediaSeedHelpers.ReplaceAsync(Fixture.SeedClient, mediaId, $"{_term}-replacement.png", MediaFixtures.UniquePng(600, 600));

        await Page.GotoAsync($"{Fixture.BaseUrl}{url}");
        await Expect(img).ToHaveAttributeAsync("src", new Regex($"^/media/{mediaId:D}/2/"));
        await Expect(img).ToHaveAttributeAsync("width", "400");
        await Expect(img).ToHaveAttributeAsync("height", "400");
        await AssertSignedCopiesLoadAsync(img);
    }

    [Fact]
    public async Task AnOlderInlineDataImage_StillShows_AndTheEditorDoesNotUploadIt()
    {
        const string dataUri = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";
        await SignInAsAdminAsync();
        var (pageId, url) = await NewRichTextPageAsync($"<p><img src=\"{dataUri}\" alt=\"Dot\"></p>");
        await CmsSeedHelpers.PublishDraftAsync(Fixture.SeedClient, pageId);

        var uploads = 0;
        Page.Request += (_, request) => { if (request.Url.Contains("/admin/media/picker/upload")) uploads++; };
        await OpenEditorAsync(pageId);
        await Expect(EditorBody.Locator("img[alt='Dot']")).ToBeVisibleAsync();
        await Page.WaitForTimeoutAsync(1500);

        // An edit and a save keep the image exactly as it was.
        await EditorBody.Locator("body p").ClickAsync();
        await Page.Keyboard.PressAsync("End");
        await Page.Keyboard.TypeAsync(" edited");
        await SaveAndPublishAsync(pageId);
        Assert.Equal(0, uploads);
        var saved = await SavedHtmlAsync(pageId);
        Assert.Contains($"src=\"{dataUri}\"", saved);
        Assert.Contains("edited", saved);
        Assert.DoesNotContain("blob:", saved);
        Assert.DoesNotContain("data-media-id", saved);

        await Page.GotoAsync($"{Fixture.BaseUrl}{url}");
        await Expect(Page.Locator("main .govuk-body img[alt='Dot']")).ToHaveAttributeAsync("src", dataUri);
    }

    [Fact]
    public async Task AnEditAndASave_LeaveLinkAddressesAsWritten()
    {
        await SignInAsAdminAsync();
        var (pageId, _) = await NewRichTextPageAsync(
            "<p><a href=\"key-dates\">Relative</a> <a href=\"/guidance/x\">Root</a> <a href=\"#top\">Anchor</a> <a href=\"https://www.gov.uk/x\">Other site</a></p>");
        await OpenEditorAsync(pageId);

        await EditorBody.Locator("body p").ClickAsync();
        await Page.Keyboard.PressAsync("End");
        await Page.Keyboard.TypeAsync(" edited");
        await SaveAndPublishAsync(pageId);

        var saved = await SavedHtmlAsync(pageId);
        Assert.Contains("edited", saved);
        Assert.Contains("href=\"key-dates\"", saved);
        Assert.Contains("href=\"/guidance/x\"", saved);
        Assert.Contains("href=\"#top\"", saved);
        Assert.Contains("href=\"https://www.gov.uk/x\"", saved);
    }
}
