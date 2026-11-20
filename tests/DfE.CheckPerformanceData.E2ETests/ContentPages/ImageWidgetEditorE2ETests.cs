using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;

namespace DfE.CheckPerformanceData.E2ETests.ContentPages;

// The Image widget editor in a browser, as the dev admin: the media picker dialog (search,
// choose, upload, keyboard), the crop and focal point tools, the decorative checkbox, the live
// preview, and the published page that the saved values produce.
//
// The shared sample image is only chosen, never changed. Images uploaded here have unique
// e2e-{guid} names and are deleted again.
[Trait("Category", "FullRegression")]
[Collection("E2E")]
public sealed class ImageWidgetEditorE2ETests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    private readonly List<Guid> _pages = [];
    private readonly List<Guid> _images = [];

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
    private ILocator Dialog => Page.Locator("#media-picker");
    private ILocator ChooseButton => Widget.Locator("[data-image-editor-choose]");
    private ILocator Field(string prop) => Widget.Locator($"[id='wf-{prop}-0.0']");

    // Signs the browser in as the dev admin, and makes a draft page with an empty Image widget.
    private async Task<(Guid Id, string Url)> OpenEditorOnANewImagePageAsync()
    {
        var adminCookie = await AuthHelpers.ImpersonateAsAdminAsync(Fixture);
        if (!string.IsNullOrEmpty(adminCookie) && adminCookie.IndexOf('=') is var eq and > 0)
            await Context.AddCookiesAsync([new Cookie { Name = adminCookie[..eq], Value = adminCookie[(eq + 1)..], Url = Fixture.BaseUrl }]);

        var segment = $"e2e-image-editor-{Guid.NewGuid():N}";
        var id = await CmsSeedHelpers.CreatePageNodeAsync(Fixture.SeedClient, FixtureContent.RootId, "content", segment, "E2E image editor");
        _pages.Add(id);
        await CmsSeedHelpers.AddWidgetAsync(Fixture.SeedClient, id, "0.0", "image");

        await Page.GotoAsync($"{Fixture.BaseUrl}/admin/pages/{id}/edit");
        await Widget.Locator("summary").First.ClickAsync();
        await Expect(Widget.Locator("[data-image-editor-init='true']")).ToBeAttachedAsync();
        return (id, $"{FixtureContent.RootPath}/{segment}");
    }

    private async Task ChooseThroughThePickerAsync(Guid mediaId, string search)
    {
        await ChooseButton.ClickAsync();
        await Expect(Dialog).ToBeVisibleAsync();
        await Dialog.Locator("#media-picker-search").FillAsync(search);
        await Dialog.GetByRole(AriaRole.Button, new() { Name = "Search", Exact = true }).ClickAsync();
        await Dialog.Locator($"label[for='media-picker-item-{mediaId}']").ClickAsync();
        await Dialog.GetByRole(AriaRole.Button, new() { Name = "Use this image" }).ClickAsync();
        await Expect(Dialog).Not.ToBeVisibleAsync();
        await Expect(Field("mediaId")).ToHaveValueAsync(mediaId.ToString());
    }

    private async Task SaveAndPublishAsync(Guid pageId)
    {
        await Widget.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
        await Expect(Page.Locator(".govuk-error-summary")).ToHaveCountAsync(0);
        await CmsSeedHelpers.PublishDraftAsync(Fixture.SeedClient, pageId);
    }

    private static (decimal X, decimal Y) Focal(string text)
    {
        var parts = text.Split(',');
        return (decimal.Parse(parts[0], CultureInfo.InvariantCulture), decimal.Parse(parts[1], CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task AnEditor_PicksCropsAndPlacesAnImage_AndThePublishedPageShowsIt()
    {
        var sample = await MediaSeedHelpers.SeedSampleAsync(Fixture.SeedClient, "png");
        var (pageId, url) = await OpenEditorOnANewImagePageAsync();
        await Expect(ChooseButton).ToHaveTextAsync("Choose image");
        // The id field and its library link are the no-script fallback.
        await Expect(Field("mediaId")).ToBeHiddenAsync();

        await ChooseThroughThePickerAsync(sample.Id, "sample");
        await Expect(ChooseButton).ToHaveTextAsync("Change image");
        await Expect(ChooseButton).ToBeFocusedAsync();
        await Expect(Widget.Locator("[data-image-editor-name]")).Not.ToBeEmptyAsync();
        await Field("alt").FillAsync("Two coloured blocks side by side");

        // Crop to a square. The sample is 64 x 32, so the largest square is its middle half.
        await Widget.GetByRole(AriaRole.Button, new() { Name = "Crop image" }).ClickAsync();
        await Expect(Widget.Locator("[data-image-editor-cropper='ready']")).ToBeAttachedAsync();
        await Widget.Locator("label[for='ie-shape-0.0-1-1']").ClickAsync();
        await Expect(Field("crop")).ToHaveValueAsync("0.25,0,0.5,1");
        await Expect(Field("aspect")).ToHaveValueAsync("1:1");

        // Set the focal point with a click, then move it with the keyboard.
        await Widget.GetByRole(AriaRole.Button, new() { Name = "Set focal point" }).ClickAsync();
        var focalImage = Widget.Locator("[data-image-editor-focal-img]");
        await Expect(focalImage).ToBeVisibleAsync();
        var box = (await focalImage.BoundingBoxAsync())!;
        await focalImage.ClickAsync(new() { Position = new() { X = box.Width * 0.3f, Y = box.Height * 0.5f } });
        var clicked = Focal(await Field("focal").InputValueAsync());
        Assert.InRange(clicked.X, 0.25m, 0.35m);
        await Widget.Locator("[data-image-editor-focal-area]").FocusAsync();
        await Page.Keyboard.PressAsync("ArrowRight");
        var moved = Focal(await Field("focal").InputValueAsync());
        Assert.Equal(clicked.X + 0.01m, moved.X);
        Assert.Equal(clicked.Y, moved.Y);
        await Expect(Widget.Locator("[data-image-editor-focal-status]")).ToContainTextAsync("across");

        await Widget.Locator("label[for='wf-width-0.0-one-half']").ClickAsync();

        // The live preview is the widget's own view, with the unsaved values.
        var preview = Widget.Locator("[data-image-editor-preview-body] figure.app-image--one-half img");
        await Expect(preview).ToHaveAttributeAsync("src", new Regex("crop=0\\.25,0,0\\.5,1.*&sig="));

        var focal = await Field("focal").InputValueAsync();
        await SaveAndPublishAsync(pageId);

        await Page.GotoAsync($"{Fixture.BaseUrl}{url}");
        var figure = Page.Locator("main figure.app-image");
        await Expect(figure).ToHaveClassAsync(new Regex("app-image--one-half"));
        var img = figure.Locator("img");
        await Expect(img).ToHaveAttributeAsync("alt", "Two coloured blocks side by side");
        var src = await img.GetAttributeAsync("src");
        Assert.Matches($"^/media/{sample.Id:D}/\\d+/[^?]+\\.webp\\?", src);
        Assert.Contains("crop=0.25,0,0.5,1", src);
        Assert.Contains("ar=1:1", src);
        Assert.Contains($"focal={focal}", src);
        Assert.Contains("&sig=", src);
        var response = await Page.APIRequest.GetAsync($"{Fixture.BaseUrl}{src}");
        Assert.Equal(200, response.Status);
    }

    [Fact]
    public async Task AnEditor_CanUploadAnImage_FromInsideThePicker()
    {
        await OpenEditorOnANewImagePageAsync();
        var name = $"e2e-{Guid.NewGuid():N}-picker-upload.png";

        await ChooseButton.ClickAsync();
        await Expect(Dialog).ToBeVisibleAsync();
        await Dialog.Locator("#media-picker-file").SetInputFilesAsync(new FilePayload
        {
            Name = name,
            MimeType = "image/png",
            Buffer = MediaFixtures.UniquePng(),
        });
        await Dialog.GetByRole(AriaRole.Button, new() { Name = "Upload", Exact = true }).ClickAsync();
        await Expect(Dialog.Locator("[data-media-picker-status]")).ToContainTextAsync($"{name} uploaded and selected");
        await Dialog.GetByRole(AriaRole.Button, new() { Name = "Use this image" }).ClickAsync();
        await Expect(Dialog).Not.ToBeVisibleAsync();

        var mediaId = await Field("mediaId").InputValueAsync();
        Assert.True(Guid.TryParse(mediaId, out var id));
        _images.Add(id);
        await Expect(Widget.Locator("[data-image-editor-name]")).ToHaveTextAsync(name);
    }

    [Fact]
    public async Task ADecorativeImage_ClearsTheAltText_AndThePageWritesAnEmptyAlt()
    {
        var sample = await MediaSeedHelpers.SeedSampleAsync(Fixture.SeedClient, "png");
        var (pageId, url) = await OpenEditorOnANewImagePageAsync();
        await ChooseThroughThePickerAsync(sample.Id, "sample");
        await Field("alt").FillAsync("Text that a decorative image drops");

        await Widget.Locator("label[for='wf-decorative-0.0']").ClickAsync();
        await Expect(Field("alt")).ToHaveValueAsync("");
        await Expect(Field("alt")).Not.ToBeEditableAsync();

        await SaveAndPublishAsync(pageId);
        await Page.GotoAsync($"{Fixture.BaseUrl}{url}");
        await Expect(Page.Locator("main figure.app-image img")).ToHaveAttributeAsync("alt", "");
    }

    [Fact]
    public async Task ThePicker_WorksFromTheKeyboard_AndGivesFocusBack()
    {
        await MediaSeedHelpers.SeedSampleAsync(Fixture.SeedClient, "png");
        await OpenEditorOnANewImagePageAsync();

        // Escape closes the dialog and focus goes back to the button that opened it.
        await ChooseButton.FocusAsync();
        await Page.Keyboard.PressAsync("Enter");
        await Expect(Dialog).ToBeVisibleAsync();
        await Expect(Dialog.Locator("#media-picker-search")).ToBeFocusedAsync();
        await Page.Keyboard.PressAsync("Escape");
        await Expect(Dialog).Not.ToBeVisibleAsync();
        await Expect(ChooseButton).ToBeFocusedAsync();

        // Tab to the first image and choose it with Enter.
        await Page.Keyboard.PressAsync("Enter");
        await Expect(Dialog).ToBeVisibleAsync();
        await Expect(Dialog.Locator("input[type='radio']").First).ToBeAttachedAsync();
        var onAnImage = false;
        for (var i = 0; i < 8 && !onAnImage; i++)
        {
            await Page.Keyboard.PressAsync("Tab");
            onAnImage = await Page.EvaluateAsync<bool>("() => document.activeElement && document.activeElement.name === 'media-picker-item'");
        }
        Assert.True(onAnImage, "Tab did not reach an image in the picker.");
        var chosen = await Page.EvaluateAsync<string>("() => document.activeElement.value");

        await Page.Keyboard.PressAsync("Enter");

        await Expect(Dialog).Not.ToBeVisibleAsync();
        await Expect(ChooseButton).ToBeFocusedAsync();
        await Expect(Field("mediaId")).ToHaveValueAsync(chosen);
    }

    // A refused save of a newly chosen image shows the errors, and the editor still shows that
    // image and its tools, not the stored (empty) widget.
    [Fact]
    public async Task ARefusedSave_AfterChoosingAnImage_KeepsTheChosenImageInTheEditor()
    {
        var sample = await MediaSeedHelpers.SeedSampleAsync(Fixture.SeedClient, "png");
        await OpenEditorOnANewImagePageAsync();
        await ChooseThroughThePickerAsync(sample.Id, "sample");
        await Field("alt").FillAsync("");
        await Widget.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();

        await Expect(Page.Locator(".govuk-error-summary")).ToContainTextAsync("Enter alt text");
        await Expect(Widget.Locator("[data-image-editor-init='true']")).ToBeAttachedAsync();
        await Expect(Field("mediaId")).ToHaveValueAsync(sample.Id.ToString());
        await Expect(ChooseButton).ToHaveTextAsync("Change image");
        await Expect(Widget.Locator("[data-image-editor-name]")).Not.ToBeEmptyAsync();
        await Expect(Widget.GetByRole(AriaRole.Button, new() { Name = "Crop image" })).ToBeVisibleAsync();
    }

    // The crop box is dragged with a mouse, so the same values can be typed. A typed crop moves
    // the crop box.
    [Fact]
    public async Task TheCrop_CanBeTyped_AndTheCropBoxFollows()
    {
        var sample = await MediaSeedHelpers.SeedSampleAsync(Fixture.SeedClient, "png");
        await OpenEditorOnANewImagePageAsync();
        await ChooseThroughThePickerAsync(sample.Id, "sample");
        await Widget.GetByRole(AriaRole.Button, new() { Name = "Crop image" }).ClickAsync();
        await Expect(Widget.Locator("[data-image-editor-cropper='ready']")).ToBeAttachedAsync();

        await Widget.Locator("[data-image-editor-manual] summary").ClickAsync();
        await Field("crop").FillAsync("0,0,0.5,1");
        await Field("crop").PressAsync("Tab");

        await Page.WaitForFunctionAsync(
            "() => { const b = document.querySelector('[id=\"widget-0.0\"] .cropper-crop-box'); const c = document.querySelector('[id=\"widget-0.0\"] .cropper-canvas');"
            + " return b && c && Math.abs(b.getBoundingClientRect().width / c.getBoundingClientRect().width - 0.5) < 0.02; }");
        await Expect(Field("crop")).ToHaveValueAsync("0,0,0.5,1");
    }

    [SkippableFact]
    public async Task TheEditorAndThePicker_HaveNoWcagViolations()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Linux), "Playwright browser test Linux-only");
        var sample = await MediaSeedHelpers.SeedSampleAsync(Fixture.SeedClient, "png");
        await OpenEditorOnANewImagePageAsync();
        await ChooseThroughThePickerAsync(sample.Id, "sample");
        await Widget.GetByRole(AriaRole.Button, new() { Name = "Crop image" }).ClickAsync();
        await Expect(Widget.Locator("[data-image-editor-cropper='ready']")).ToBeAttachedAsync();
        await AxeChecks.ExpectNoViolationsAsync(Page, "image widget editor");

        await ChooseButton.ClickAsync();
        await Expect(Dialog.Locator("input[type='radio']").First).ToBeAttachedAsync();
        await AxeChecks.ExpectNoViolationsAsync(Page, "media picker");
    }
}
