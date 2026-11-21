using System.Net;
using System.Text.RegularExpressions;
using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;

namespace DfE.CheckPerformanceData.E2ETests.ContentPages;

// The media library's pages under /development-testing, where a developer tries each part of the
// library by hand. The guide page links to every part; each link must lead somewhere. The sample
// images arrive with the fixture pages, so the pages that show them have them on a new stack.
//
// The guide's SVG samples are uploaded here as a developer would: the hostile one is accepted
// and cleaned, the one with a DTD is refused. The image that upload makes is deleted again.
[Trait("Category", "FullRegression")]
[Collection("E2E")]
public sealed partial class MediaLibraryTestPagesE2ETests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    private const string GuidePath = $"{FixtureContent.RootPath}/media-library-test-page";
    private const string RichTextPath = $"{FixtureContent.RootPath}/media-rich-text-test-page";
    private const string EmbeddedPath = $"{FixtureContent.RootPath}/embedded-image-test-page";
    private const string SamplePhoto = "00000000-cd94-4a01-8f01-0000000e0203";

    private readonly List<Guid> _images = [];

    public override async Task DisposeAsync()
    {
        try
        {
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

    // The guide's links to resized copies are there to show the errors: a missing or changed
    // signature, and a width outside the fixed set.
    [GeneratedRegex(@"^/media/[0-9a-f-]{36}/\d+/[^?]+\?w=")]
    private static partial Regex RefusedCopy();

    [Fact]
    public async Task TheGuide_Renders_AndEveryLinkOnItResolves()
    {
        await SignInAsAdminAsync();
        await Page.GotoAsync($"{Fixture.BaseUrl}{GuidePath}");
        await Expect(Page.Locator("h1")).ToHaveTextAsync("Media library test page");

        var hrefs = (await Page.Locator("main a[href^='/']").EvaluateAllAsync<string[]>("els => els.map(e => e.getAttribute('href'))"))
            .Distinct().ToList();
        Assert.True(hrefs.Count >= 20, $"the guide has only {hrefs.Count} links");

        var refused = 0;
        foreach (var href in hrefs)
        {
            var response = await Page.APIRequest.GetAsync($"{Fixture.BaseUrl}{href}");
            var expected = RefusedCopy().IsMatch(href) ? 400 : 200;
            if (expected == 400) refused++;
            Assert.True(response.Status == expected, $"{href} answered {response.Status}, not {expected}");
        }
        Assert.Equal(3, refused);
    }

    [Fact]
    public async Task TheRichTextTestPage_ShowsEachLibraryImage_AndKeepsTheDecorativeOne()
    {
        await Page.GotoAsync($"{Fixture.BaseUrl}{RichTextPath}");

        var images = Page.Locator("main img[data-media-id]");
        await Expect(images).ToHaveCountAsync(6);
        foreach (var src in await images.EvaluateAllAsync<string[]>("els => els.map(e => e.getAttribute('src'))"))
        {
            var response = await Page.APIRequest.GetAsync($"{Fixture.BaseUrl}{src}");
            Assert.True(response.Status == 200, $"{src} answered {response.Status}");
        }
        var decorative = Page.Locator("main img[role='presentation']");
        await Expect(decorative).ToHaveAttributeAsync("alt", "");
        await Expect(decorative).ToHaveAttributeAsync("src", new Regex("sig="));
    }

    // The Image test page's images are the reserved samples, so a new stack shows them.
    [Fact]
    public async Task TheImageTestPage_ShowsTheSampleImages()
    {
        await Page.GotoAsync($"{Fixture.BaseUrl}{FixtureContent.WidgetsPath}/image-test-page");

        await Expect(Page.Locator("main figure.app-image img").First).ToBeVisibleAsync();
        Assert.True(await Page.Locator("main figure.app-image img").CountAsync() >= 4);
    }

    // The move runs in other tests, so the page may already show a library image instead.
    [Fact]
    public async Task TheEmbeddedImageTestPage_ShowsItsImage()
    {
        await Page.GotoAsync($"{Fixture.BaseUrl}{EmbeddedPath}");

        await Expect(Page.Locator("main img[alt='An orange block beside a purple block']")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task TheFixtureContentBlock_ShowsTheSamplePhoto()
    {
        await SignInAsAdminAsync();
        await Page.GotoAsync($"{Fixture.BaseUrl}/content-block/versions/development-testing-image");

        // Each version is shown in a closed details block, so the image is there but not shown.
        var img = Page.Locator($"img[data-media-id='{SamplePhoto}']").First;
        await Expect(img).ToBeAttachedAsync();
        await Expect(img).ToHaveAttributeAsync("src", new Regex("sig="));
    }

    [Fact]
    public async Task TheGuidesHostileSvg_IsCleanedOnUpload_AndItsDtdSample_IsRefused()
    {
        await SignInAsAdminAsync();
        await Page.GotoAsync($"{Fixture.BaseUrl}{GuidePath}");
        var samples = await Page.Locator("main pre code").AllTextContentsAsync();
        Assert.Equal(2, samples.Count);

        var id = await MediaSeedHelpers.UploadAsync(Fixture.SeedClient, $"e2e-{Guid.NewGuid():N}-hostile.svg",
            System.Text.Encoding.UTF8.GetBytes(samples[0]), "image/svg+xml");
        _images.Add(id);
        var stored = await (await Page.APIRequest.GetAsync($"{Fixture.BaseUrl}/media/{id:D}/1/hostile.svg")).TextAsync();
        Assert.Contains("<rect", stored);
        foreach (var gone in new[] { "<script", "onload", "javascript:", "foreignObject", "example.com", "@import" })
            Assert.DoesNotContain(gone, stored, StringComparison.OrdinalIgnoreCase);

        var refusal = await UploadExpectingRefusalAsync($"e2e-{Guid.NewGuid():N}-entity.svg", samples[1]);
        Assert.Contains("\"outcomeName\":\"rejected\"", refusal);
    }

    // The library's upload, answered in JSON, for a file it refuses. Returns the response body.
    private async Task<string> UploadExpectingRefusalAsync(string fileName, string svg)
    {
        var client = Fixture.SeedClient;
        var (token, cookie) = await AntiforgeryHelpers.ScrapeAsync(client, "/admin/media");
        var file = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(svg));
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/svg+xml");
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(client.BaseAddress!, "/admin/media/upload"))
        {
            Content = new MultipartFormDataContent { { file, "files", fileName } },
        };
        request.Headers.Add("Accept", "application/json");
        request.Headers.Add("X-XSRF-TOKEN", token);
        request.Headers.Add("Cookie", cookie);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }
}
