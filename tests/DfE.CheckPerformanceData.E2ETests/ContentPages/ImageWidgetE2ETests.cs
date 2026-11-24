using System.Text.RegularExpressions;
using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;

namespace DfE.CheckPerformanceData.E2ETests.ContentPages;

// The Image widget on a published page in a browser. The page is built through the editor's own
// endpoints, so the props pass the same save checks an editor's form does. The image URLs must be
// signed and must load, the alt text and caption must be there, and replacing the image in the
// library must change the live page.
//
// The shared sample image is never replaced, because other tests rely on it. The replace test
// uploads its own image and deletes it again.
[Trait("Category", "FullRegression")]
[Collection("E2E")]
public sealed class ImageWidgetE2ETests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    private readonly List<Guid> _pages = [];
    private readonly List<Guid> _images = [];

    public override async Task DisposeAsync()
    {
        try
        {
            if (_images.Count > 0)
            {
                await AuthHelpers.ImpersonateAsAdminAsync(Fixture);
                foreach (var id in _images) await MediaSeedHelpers.ClearUsagesAsync(Fixture.SeedClient, id);
                foreach (var id in _images) await MediaSeedHelpers.DeleteAsync(Fixture.SeedClient, id);
            }
            for (var i = _pages.Count - 1; i >= 0; i--)
                await CmsSeedHelpers.TryDeletePageAsync(Fixture.SeedClient, _pages[i]);
        }
        finally
        {
            if (_images.Count > 0) await AuthHelpers.ImpersonateAsEditorAsync(Fixture);
            await base.DisposeAsync();
        }
    }

    private async Task<(Guid Id, string Url)> PublishImagePageAsync(Guid mediaId, Dictionary<string, string>? extra = null)
    {
        var segment = $"e2e-image-{Guid.NewGuid():N}";
        var id = await CmsSeedHelpers.CreatePageNodeAsync(
            Fixture.SeedClient, FixtureContent.RootId, "content", segment, "E2E image widget");
        _pages.Add(id);

        await CmsSeedHelpers.AddWidgetAsync(Fixture.SeedClient, id, "0.0", "image");
        var props = new Dictionary<string, string>
        {
            ["mediaId"] = mediaId.ToString(),
            ["alt"] = "Two coloured blocks side by side",
            ["decorative"] = "false",
            ["caption"] = "Sample caption",
            ["credit"] = "Sample credit",
            ["width"] = "one-half",
            ["align"] = "",
            ["href"] = "",
            ["crop"] = "",
            ["aspect"] = "",
            ["focal"] = "",
        };
        foreach (var (key, value) in extra ?? []) props[key] = value;
        await CmsSeedHelpers.UpdateWidgetAsync(Fixture.SeedClient, id, "0.0", "image", props);
        await CmsSeedHelpers.PublishDraftAsync(Fixture.SeedClient, id);

        return (id, $"{FixtureContent.RootPath}/{segment}");
    }

    private ILocator Figure => Page.Locator("main figure.app-image");

    private async Task AssertLoadsAsImageAsync(string url)
    {
        var response = await Page.APIRequest.GetAsync($"{Fixture.BaseUrl}{url}");
        Assert.Equal(200, response.Status);
        Assert.StartsWith("image/", response.Headers["content-type"]);
    }

    [Fact]
    [Trait("Category", "Smoke")]
    public async Task APublishedImage_HasSignedUrlsThatLoad_ItsAltText_AndItsCaption()
    {
        var sample = await MediaSeedHelpers.SeedSampleAsync(Fixture.SeedClient, "png");
        var (_, url) = await PublishImagePageAsync(sample.Id, new() { ["aspect"] = "1:1" });

        var response = await Page.GotoAsync($"{Fixture.BaseUrl}{url}");
        Assert.Equal(200, response!.Status);

        await Expect(Figure).ToHaveClassAsync(new Regex("app-image--one-half"));
        var img = Figure.Locator("img");
        await Expect(img).ToHaveAttributeAsync("alt", "Two coloured blocks side by side");
        await Expect(img).ToHaveAttributeAsync("loading", "lazy");
        // The 64 x 32 sample cut to a square is 32 x 32.
        await Expect(img).ToHaveAttributeAsync("width", "32");
        await Expect(img).ToHaveAttributeAsync("height", "32");
        await Expect(Figure.Locator("figcaption")).ToHaveTextAsync("Sample caption — Sample credit");

        var src = await img.GetAttributeAsync("src");
        Assert.Matches($"^/media/{sample.Id:D}/\\d+/[^?]+\\.webp\\?.*ar=1:1.*&sig=", src);
        await AssertLoadsAsImageAsync(src!);

        var srcset = await img.GetAttributeAsync("srcset");
        Assert.False(string.IsNullOrEmpty(srcset));
        foreach (var entry in srcset!.Split(", "))
        {
            var entryUrl = entry.Split(' ')[0];
            Assert.Contains("sig=", entryUrl);
            await AssertLoadsAsImageAsync(entryUrl);
        }

        // The browser itself loads the image. It is lazy, so wait for it rather than check once.
        await img.ScrollIntoViewIfNeededAsync();
        await Page.WaitForFunctionAsync(
            "() => { const i = document.querySelector('main figure.app-image img'); return i && i.complete && i.naturalWidth > 0; }");
    }

    [Fact]
    public async Task AnSvg_IsShownFromItsOriginalFile()
    {
        var sample = await MediaSeedHelpers.SeedSampleAsync(Fixture.SeedClient, "svg");
        var (_, url) = await PublishImagePageAsync(sample.Id);

        await Page.GotoAsync($"{Fixture.BaseUrl}{url}");

        var img = Figure.Locator("img");
        var src = await img.GetAttributeAsync("src");
        Assert.Matches($"^/media/{sample.Id:D}/\\d+/[^?]+\\.svg$", src);
        Assert.Null(await img.GetAttributeAsync("srcset"));
        await AssertLoadsAsImageAsync(src!);
    }

    [Fact]
    public async Task ReplacingTheImage_ChangesTheLivePage()
    {
        var adminCookie = await AuthHelpers.ImpersonateAsAdminAsync(Fixture);
        if (!string.IsNullOrEmpty(adminCookie) && adminCookie.IndexOf('=') is var eq and > 0)
            await Context.AddCookiesAsync([new Cookie { Name = adminCookie[..eq], Value = adminCookie[(eq + 1)..], Url = Fixture.BaseUrl }]);
        var imageId = await MediaSeedHelpers.UploadAsync(Fixture.SeedClient, $"e2e-{Guid.NewGuid():N}-widget.png", MediaFixtures.UniquePng());
        _images.Add(imageId);
        var (_, url) = await PublishImagePageAsync(imageId);

        await Page.GotoAsync($"{Fixture.BaseUrl}{url}");
        Assert.StartsWith($"/media/{imageId:D}/1/", await Figure.Locator("img").GetAttributeAsync("src"));

        await MediaSeedHelpers.ReplaceAsync(Fixture.SeedClient, imageId, "replacement.png", MediaFixtures.UniquePng());

        await Page.GotoAsync($"{Fixture.BaseUrl}{url}");
        var src = await Figure.Locator("img").GetAttributeAsync("src");
        Assert.StartsWith($"/media/{imageId:D}/2/", src);
        await AssertLoadsAsImageAsync(src!);
    }

    [Fact]
    public async Task SavingAnImageWithNoAltText_ShowsTheErrorInTheEditor_AndSavesNothing()
    {
        var sample = await MediaSeedHelpers.SeedSampleAsync(Fixture.SeedClient, "png");
        var (id, _) = await PublishImagePageAsync(sample.Id);

        await Page.GotoAsync($"{Fixture.BaseUrl}/admin/pages/{id}/edit");
        var widget = Page.Locator("[id='widget-0.0']");
        await widget.Locator("summary").First.ClickAsync();
        await widget.Locator("[id='wf-alt-0.0']").FillAsync("");
        await widget.Locator("[id='wf-caption-0.0']").FillAsync("A caption that is not saved");
        await widget.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();

        var summary = Page.Locator(".govuk-error-summary");
        await Expect(summary).ToContainTextAsync("Enter alt text, or select that the image is decorative");
        await Expect(summary.Locator("a")).ToHaveAttributeAsync("href", "#wf-alt-0.0");
        await Expect(widget.Locator(".govuk-error-message")).ToContainTextAsync("Enter alt text");
        // The posted values come back in the form, and the stored widget is unchanged.
        await Expect(widget.Locator("[id='wf-caption-0.0']")).ToHaveValueAsync("A caption that is not saved");
        await Expect(widget.Locator(".cpb-preview figcaption")).ToHaveTextAsync("Sample caption — Sample credit");
    }
}
