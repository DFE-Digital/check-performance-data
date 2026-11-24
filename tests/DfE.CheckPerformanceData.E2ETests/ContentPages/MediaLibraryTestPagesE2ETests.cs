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

    // The guide's folder steps need the sample folders: Samples holds Graphics (the PNG and the
    // SVG) and Photos (the photo). The start puts the samples back in them.
    [Fact]
    public async Task TheSampleFolders_HoldTheSampleImages_AndShowInTheAdminNav()
    {
        await SignInAsAdminAsync();
        await Page.GotoAsync($"{Fixture.BaseUrl}/admin/media?folder=00000000-cd94-4a01-8f01-0000000e0302&subfolders=false");

        await Expect(Page.Locator("h1")).ToHaveTextAsync("Graphics");
        await Expect(Page.Locator("[data-media-item='00000000-cd94-4a01-8f01-0000000e0201']")).ToBeVisibleAsync();
        await Expect(Page.Locator("[data-media-item='00000000-cd94-4a01-8f01-0000000e0202']")).ToBeVisibleAsync();
        await Expect(Page.Locator("#admin-rail a[href='/admin/media?folder=00000000-cd94-4a01-8f01-0000000e0301']")).ToHaveTextAsync("Samples");
        await Page.GotoAsync($"{Fixture.BaseUrl}/admin/media?folder=00000000-cd94-4a01-8f01-0000000e0303&subfolders=false");
        await Expect(Page.Locator($"[data-media-item='{SamplePhoto}']")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task TheRichTextTestPage_ShowsEachLibraryImage_AndKeepsTheDecorativeOne()
    {
        await Page.GotoAsync($"{Fixture.BaseUrl}{RichTextPath}");

        var images = Page.Locator("main img[data-media-id]");
        await Expect(images).ToHaveCountAsync(9);
        foreach (var src in await images.EvaluateAllAsync<string[]>("els => els.map(e => e.getAttribute('src'))"))
        {
            var response = await Page.APIRequest.GetAsync($"{Fixture.BaseUrl}{src}");
            Assert.True(response.Status == 200, $"{src} answered {response.Status}");
        }
        var decorative = Page.Locator("main img[role='presentation']");
        await Expect(decorative).ToHaveAttributeAsync("alt", "");
        await Expect(decorative).ToHaveAttributeAsync("src", new Regex("sig="));
    }

    // The sample PNG is twice as wide as it is high. The editor stretched it into a 320 by 320 box,
    // and the page keeps that box after the file loads.
    [Fact]
    public async Task TheRichTextTestPage_ShowsTheStretchedImageInTheBoxTheEditorSet()
    {
        await Page.GotoAsync($"{Fixture.BaseUrl}{RichTextPath}");

        var img = Page.Locator("main img[alt='Coloured blocks, stretched into a square']");
        await img.ScrollIntoViewIfNeededAsync();
        await img.EvaluateAsync("i => i.complete && i.naturalWidth > 0 ? null : new Promise(r => i.addEventListener('load', r, { once: true }))");
        Assert.True(await img.EvaluateAsync<bool>("i => i.naturalWidth === 2 * i.naturalHeight"), "The file is twice as wide as it is high.");
        // The size inside the image's border.
        var size = await img.EvaluateAsync<double[]>("i => [i.clientWidth, i.clientHeight]");
        Assert.InRange(size[0], 319, 321);
        Assert.InRange(size[1], 319, 321);
    }

    // Two marked boxes on the same wide file: "follow" takes the file's shape at its width, and
    // "fixed" keeps the square that the editor set.
    [Theory]
    [InlineData("Coloured blocks, following the shape of the file", 320, 160)]
    [InlineData("Coloured blocks, kept in a square box", 200, 200)]
    public async Task TheRichTextTestPage_ShowsEachMarkedBoxInItsShape(string alt, int width, int height)
    {
        await Page.GotoAsync($"{Fixture.BaseUrl}{RichTextPath}");

        var img = Page.Locator($"main img[alt='{alt}']");
        await img.ScrollIntoViewIfNeededAsync();
        await img.EvaluateAsync("i => i.complete && i.naturalWidth > 0 ? null : new Promise(r => i.addEventListener('load', r, { once: true }))");
        var size = await img.EvaluateAsync<double[]>("i => [i.clientWidth, i.clientHeight]");
        Assert.InRange(size[0], width - 1, width + 1);
        Assert.InRange(size[1], height - 1, height + 1);
    }

    // The Image test page crops the sample PNG in more than one way. Its image page lists each way
    // under "Crops and sizes", with a preview made from the original, and links to the pages. The
    // grid marks the image with a link to that section.
    [Fact]
    public async Task TheSamplePng_ListsItsCropsAndSizes_WithWorkingPreviewsAndPageLinks()
    {
        const string samplePng = "00000000-cd94-4a01-8f01-0000000e0201";
        await SignInAsAdminAsync();
        await Page.GotoAsync($"{Fixture.BaseUrl}/admin/media?folder=00000000-cd94-4a01-8f01-0000000e0302");

        var badge = Page.Locator($"[data-media-item='{samplePng}'] [data-media-variant-count]");
        await Expect(badge).ToContainTextAsync(new Regex(@"^Used in \d+ ways"));
        await badge.ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex($"/admin/media/{samplePng}#crops-and-sizes$"));
        var section = Page.Locator("#crops-and-sizes");
        await Expect(section.Locator("h2")).ToHaveTextAsync("Crops and sizes");
        await Expect(section).ToContainTextAsync("It is not a separate file.");

        var cards = section.Locator("[data-media-variant]");
        Assert.True(await cards.CountAsync() >= 2, "The sample PNG is used in at least two ways.");
        var titles = await section.Locator("[data-media-variant-title]").AllTextContentsAsync();
        Assert.Contains("Original, not cropped", titles);
        Assert.Contains(titles, t => t.StartsWith("Cropped to", StringComparison.Ordinal));
        await Expect(section.Locator("[data-media-variant-title]", new() { HasText = "Cropped to 16:9, focal point centre-left" })).ToBeVisibleAsync();

        // Each preview is a signed copy that loads, with alt text that names the crop.
        var previews = section.Locator("img[data-media-variant-preview]");
        for (var i = 0; i < await previews.CountAsync(); i++)
        {
            var img = previews.Nth(i);
            await img.ScrollIntoViewIfNeededAsync();
            await img.EvaluateAsync("i => i.complete && i.naturalWidth > 0 ? null : new Promise(r => i.addEventListener('load', r, { once: true }))");
            Assert.True(await img.EvaluateAsync<bool>("i => i.naturalWidth > 0"), $"preview {i} did not load");
            Assert.StartsWith("blocks.png, ", await img.GetAttributeAsync("alt"));
            Assert.Contains("sig=", await img.GetAttributeAsync("src"));
        }

        // The cropped copy really has the shape of the crop.
        var cut = section.Locator("[data-media-variant]", new() { Has = Page.Locator("[data-media-variant-title]", new() { HasText = "16:9" }) })
            .Locator("img[data-media-variant-preview]");
        var ratio = await cut.EvaluateAsync<double>("i => i.naturalWidth / i.naturalHeight");
        Assert.InRange(ratio, 1.70, 1.85);

        // Each page link opens its editor, with the page's state beside it.
        var links = await section.Locator("[data-media-variant-uses] a").EvaluateAllAsync<string[]>("els => els.map(e => e.getAttribute('href'))");
        Assert.Contains(links, l => l.StartsWith("/admin/pages/", StringComparison.Ordinal));
        foreach (var href in links.Distinct())
        {
            var response = await Page.APIRequest.GetAsync($"{Fixture.BaseUrl}{href}");
            Assert.True(response.Status == 200, $"{href} answered {response.Status}");
        }
        await Expect(section.Locator("[data-media-variant-state]").First).ToHaveTextAsync(new Regex("^(Live|Scheduled|Draft|Content block)$"));
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
