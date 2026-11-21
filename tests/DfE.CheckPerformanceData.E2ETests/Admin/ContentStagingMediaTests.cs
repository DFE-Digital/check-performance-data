using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;

namespace DfE.CheckPerformanceData.E2ETests.Admin;

// A content staging bundle carries the media library images its pages use. The test exports a
// page with an Image widget and a page with a library image in rich text, gives the pages and
// both images new ids in the bundle (as if they came from another environment), imports it
// through the admin screens, and checks that the imported pages show the imported images and
// that the library lists where each one is used.
[Trait("Category", "FullRegression")]
[Collection("E2E")]
public sealed class ContentStagingMediaTests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    private readonly List<Guid> _pages = [];
    private readonly List<Guid> _images = [];
    private readonly string _run = Guid.NewGuid().ToString("N")[..8];

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

    private async Task SignInAsAdminAsync()
    {
        var adminCookie = await AuthHelpers.ImpersonateAsAdminAsync(Fixture);
        if (!string.IsNullOrEmpty(adminCookie) && adminCookie.IndexOf('=') is var eq and > 0)
            await Context.AddCookiesAsync([new Microsoft.Playwright.Cookie { Name = adminCookie[..eq], Value = adminCookie[(eq + 1)..], Url = Fixture.BaseUrl }]);
    }

    private async Task<Guid> UploadAsync(int width, int height)
    {
        var id = await MediaSeedHelpers.UploadAsync(Fixture.SeedClient, $"e2e-staging-{_run}-{width}x{height}.png", MediaFixtures.UniquePng(width, height));
        _images.Add(id);
        return id;
    }

    private async Task<Guid> PageAsync(string segment, string title, string widgetType, Dictionary<string, string> props)
    {
        var id = await CmsSeedHelpers.CreatePageNodeAsync(Fixture.SeedClient, FixtureContent.RootId, "content", segment, title);
        _pages.Add(id);
        await CmsSeedHelpers.AddWidgetAsync(Fixture.SeedClient, id, "0.0", widgetType);
        await CmsSeedHelpers.UpdateWidgetAsync(Fixture.SeedClient, id, "0.0", widgetType, props);
        await CmsSeedHelpers.PublishDraftAsync(Fixture.SeedClient, id);
        return id;
    }

    private async Task<string> ExportAsync(params Guid[] pageIds)
    {
        var (token, cookie) = await AntiforgeryHelpers.ScrapeAsync(Fixture.SeedClient, "/dev/antiforgery-token");
        var fields = pageIds.Select(id => new KeyValuePair<string, string>("pageNodeIds", id.ToString()))
            .Append(new("fullHistory", "true"))
            .Append(new("__RequestVerificationToken", token));
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(Fixture.SeedClient.BaseAddress!, "/admin/content-staging/export"))
        {
            Content = new FormUrlEncodedContent(fields),
        };
        request.Headers.Add("Cookie", cookie);

        using var response = await Fixture.SeedClient.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var body = await response.Content.ReadAsStreamAsync();
        using var archive = new ZipArchive(body, ZipArchiveMode.Read);
        using var reader = new StreamReader(archive.GetEntry("bundle.json")!.Open());
        return await reader.ReadToEndAsync();
    }

    private async Task AssertLoadsAsImageAsync(string? url)
    {
        Assert.False(string.IsNullOrEmpty(url));
        var response = await Page.APIRequest.GetAsync($"{Fixture.BaseUrl}{url}");
        Assert.Equal(200, response.Status);
        Assert.StartsWith("image/", response.Headers["content-type"]);
    }

    [Fact]
    public async Task An_imported_bundle_brings_its_images_and_the_pages_show_them()
    {
        await SignInAsAdminAsync();
        var widgetImage = await UploadAsync(64, 32);
        var richTextImage = await UploadAsync(40, 40);
        var imageSegment = $"e2e-staging-image-{_run}";
        var textSegment = $"e2e-staging-text-{_run}";
        var imagePage = await PageAsync(imageSegment, $"Staged image {_run}", "image", new()
        {
            ["mediaId"] = widgetImage.ToString(), ["alt"] = "Staged widget image", ["decorative"] = "false",
            ["caption"] = "", ["credit"] = "", ["width"] = "one-half", ["align"] = "", ["href"] = "",
            ["crop"] = "", ["aspect"] = "", ["focal"] = "",
        });
        var textPage = await PageAsync(textSegment, $"Staged text {_run}", "richtext", new()
        {
            ["html"] = $"<p>Before</p><p><img data-media-id=\"{richTextImage}\" src=\"/media/{richTextImage}/x.png\" alt=\"Staged rich text image\"></p>",
        });

        var json = await ExportAsync(imagePage, textPage);
        Assert.Contains(widgetImage.ToString(), json);
        Assert.Equal(2, JsonNode.Parse(json)!["mediaAssets"]!.AsArray().Count);
        Assert.Equal(2, JsonNode.Parse(json)!["mediaFiles"]!.AsArray().Count);

        // The same content as another environment would send it: new ids for the pages and the
        // images, so the import has to bring the images in rather than find them.
        var newImagePage = Guid.NewGuid();
        var newTextPage = Guid.NewGuid();
        var newWidgetImage = Guid.NewGuid();
        var newRichTextImage = Guid.NewGuid();
        _pages.Add(newImagePage);
        _pages.Add(newTextPage);
        _images.Add(newWidgetImage);
        _images.Add(newRichTextImage);
        var moved = json
            .Replace(imagePage.ToString(), newImagePage.ToString())
            .Replace(textPage.ToString(), newTextPage.ToString())
            .Replace(widgetImage.ToString(), newWidgetImage.ToString())
            .Replace(richTextImage.ToString(), newRichTextImage.ToString())
            .Replace(imageSegment, imageSegment + "-imported")
            .Replace(textSegment, textSegment + "-imported");
        // Only the two pages: their parent already exists here.
        var bundle = JsonNode.Parse(moved)!.AsObject();
        var pages = bundle["pageNodes"]!.AsArray();
        foreach (var node in pages.Where(n => n!["id"]!.GetValue<Guid>() != newImagePage && n["id"]!.GetValue<Guid>() != newTextPage).ToList())
            pages.Remove(node);

        await Page.GotoAsync($"{Fixture.BaseUrl}/admin/content-staging");
        await Page.Locator("input[name='bundle']").SetInputFilesAsync(new FilePayload
        {
            Name = "bundle.json", MimeType = "application/json", Buffer = Encoding.UTF8.GetBytes(bundle.ToJsonString())
        });
        await Page.GetByRole(AriaRole.Button, new() { Name = "Preview import" }).ClickAsync();
        await Expect(Page.Locator("h1")).ToHaveTextAsync("Review import");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Confirm import" }).ClickAsync();
        await Page.WaitForURLAsync($"{Fixture.BaseUrl}/admin/content-staging");
        await Expect(Page.Locator("main.admin-main")).ToContainTextAsync("Images: 2 added");

        // The imported pages show the imported images, at their new ids.
        await Page.GotoAsync($"{Fixture.BaseUrl}{FixtureContent.RootPath}/{imageSegment}-imported");
        var widgetImg = Page.Locator("main figure.app-image img");
        await Expect(widgetImg).ToHaveAttributeAsync("alt", "Staged widget image");
        var widgetSrc = await widgetImg.GetAttributeAsync("src");
        Assert.Contains($"/media/{newWidgetImage:D}/", widgetSrc);
        await AssertLoadsAsImageAsync(widgetSrc);

        await Page.GotoAsync($"{Fixture.BaseUrl}{FixtureContent.RootPath}/{textSegment}-imported");
        var textImg = Page.Locator("main img[alt='Staged rich text image']");
        var textSrc = await textImg.GetAttributeAsync("src");
        Assert.Contains($"/media/{newRichTextImage:D}/", textSrc);
        await AssertLoadsAsImageAsync(textSrc);

        // The library knows where the imported images are used.
        await Page.GotoAsync($"{Fixture.BaseUrl}/admin/media/{newWidgetImage}");
        await Expect(Page.Locator("main.admin-main")).ToContainTextAsync($"Staged image {_run}");
        await Page.GotoAsync($"{Fixture.BaseUrl}/admin/media/{newRichTextImage}");
        await Expect(Page.Locator("main.admin-main")).ToContainTextAsync($"Staged text {_run}");
    }
}
