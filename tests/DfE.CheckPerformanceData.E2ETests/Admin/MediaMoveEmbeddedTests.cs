using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;

namespace DfE.CheckPerformanceData.E2ETests.Admin;

// Moving images embedded in page content into the media library, as the dev admin. A published
// page holds an inline data: PNG. The dry run lists the page; the run moves the image; the live
// page then shows signed copies from the library, the library holds the image, the page history
// has a new live version while the old version still shows its embedded image, and a second dry
// run finds nothing left on the page.
//
// The image has a unique e2e-{guid} alt text, which becomes its library name. The page and the
// image are deleted again.
[Trait("Category", "FullRegression")]
[Collection("E2E")]
public sealed class MediaMoveEmbeddedTests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    private const string MoveUrl = "/admin/media/move-embedded";
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

    private async Task SignInAsAdminAsync()
    {
        var adminCookie = await AuthHelpers.ImpersonateAsAdminAsync(Fixture);
        if (!string.IsNullOrEmpty(adminCookie) && adminCookie.IndexOf('=') is var eq and > 0)
            await Context.AddCookiesAsync([new Cookie { Name = adminCookie[..eq], Value = adminCookie[(eq + 1)..], Url = Fixture.BaseUrl }]);
    }

    // A published page with one rich-text widget that holds the given HTML.
    private async Task<(Guid Id, string Url)> PublishedRichTextPageAsync(string html)
    {
        var segment = $"e2e-move-embedded-{Guid.NewGuid():N}";
        var id = await CmsSeedHelpers.CreatePageNodeAsync(Fixture.SeedClient, FixtureContent.RootId, "content", segment, "E2E embedded image");
        _pages.Add(id);
        await CmsSeedHelpers.AddWidgetAsync(Fixture.SeedClient, id, "0.0", "richtext");
        await CmsSeedHelpers.UpdateWidgetAsync(Fixture.SeedClient, id, "0.0", "richtext", [new("html", html)]);
        await CmsSeedHelpers.PublishDraftAsync(Fixture.SeedClient, id);
        return (id, $"{FixtureContent.RootPath}/{segment}");
    }

    private ILocator PageRow(Guid pageId) => Page.Locator($"[data-move-embedded-page='{pageId}']");

    [Fact]
    public async Task TheDryRunListsAPageWithAnEmbeddedImage_TheRunMovesIt_AndTheLivePageShowsTheLibraryImage()
    {
        await SignInAsAdminAsync();
        var dataUri = "data:image/png;base64," + Convert.ToBase64String(MediaFixtures.UniquePng(320, 160));
        var alt = $"{_term} chart";
        var (pageId, url) = await PublishedRichTextPageAsync($"<p>Before</p><p><img src=\"{dataUri}\" alt=\"{alt}\" width=\"320\" height=\"160\"></p>");

        // The live page shows the image inline before the move.
        await Page.GotoAsync($"{Fixture.BaseUrl}{url}");
        await Expect(Page.Locator($"main .govuk-body img[alt='{alt}']")).ToHaveAttributeAsync("src", dataUri);

        // The dry run lists the page, and changes nothing.
        await Page.GotoAsync($"{Fixture.BaseUrl}{MoveUrl}");
        await Expect(Page.Locator("h1")).ToHaveTextAsync("Move embedded images");
        await Expect(PageRow(pageId)).ToContainTextAsync("Live");
        await Expect(PageRow(pageId)).ToContainTextAsync("Will move");
        await Expect(PageRow(pageId).Locator("td").Nth(2)).ToHaveTextAsync("1");

        // The run.
        await Page.Locator("[data-move-embedded-form] button").ClickAsync();
        await Expect(Page.Locator("h1")).ToHaveTextAsync("Embedded images moved");
        var moved = int.Parse((await Page.Locator("[data-move-embedded-result]").GetAttributeAsync("data-images-moved"))!);
        Assert.True(moved >= 1);
        await Expect(Page.Locator($"[data-move-embedded-failure='{pageId}']")).ToHaveCountAsync(0);

        // The live page now shows the library image, with signed responsive copies.
        await Page.GotoAsync($"{Fixture.BaseUrl}{url}");
        var img = Page.Locator($"main .govuk-body img[alt='{alt}']");
        await Expect(img).ToHaveAttributeAsync("src", new Regex("^/media/[0-9a-f-]{36}/1/.+sig="));
        var mediaId = Guid.Parse((await img.GetAttributeAsync("data-media-id"))!);
        _images.Add(mediaId);
        await Expect(img).ToHaveAttributeAsync("width", "320");
        var srcset = await img.GetAttributeAsync("srcset");
        Assert.False(string.IsNullOrEmpty(srcset));
        foreach (var entry in srcset!.Split(", "))
        {
            var copy = entry[..entry.LastIndexOf(' ')];
            Assert.Contains("sig=", copy);
            var response = await Page.APIRequest.GetAsync($"{Fixture.BaseUrl}{copy}");
            Assert.Equal(200, response.Status);
            Assert.StartsWith("image/", response.Headers["content-type"]);
        }

        // The library holds the image, named and described by its alt text.
        await Page.GotoAsync($"{Fixture.BaseUrl}/admin/media?search={_term}");
        await Expect(Page.Locator($"a[href='/admin/media/{mediaId}']").First).ToBeVisibleAsync();
        await Page.GotoAsync($"{Fixture.BaseUrl}/admin/media/{mediaId}");
        await Expect(Page.Locator("h1")).ToHaveTextAsync($"{alt}.png");

        // The move added a new live version. The old version stays in the history, as it was.
        await Page.GotoAsync($"{Fixture.BaseUrl}/admin/pages/{pageId}/edit");
        var history = Page.Locator("#version-history + p + table tbody tr");
        await Expect(history).ToHaveCountAsync(2);
        var newRow = history.Filter(new() { Has = Page.Locator($"a[href='/admin/pages/{pageId}/versions/2/preview']") });
        var oldRow = history.Filter(new() { Has = Page.Locator($"a[href='/admin/pages/{pageId}/versions/1/preview']") });
        await Expect(newRow.Locator(".govuk-tag")).ToHaveTextAsync("Live");
        await Expect(oldRow.Locator(".govuk-tag")).ToHaveTextAsync("Past");

        // The old version still shows its embedded image. The new one shows the library image.
        await Page.GotoAsync($"{Fixture.BaseUrl}/admin/pages/{pageId}/versions/1/preview");
        await Expect(Page.Locator($"main img[alt='{alt}']")).ToHaveAttributeAsync("src", dataUri);
        await Page.GotoAsync($"{Fixture.BaseUrl}/admin/pages/{pageId}/versions/2/preview");
        await Expect(Page.Locator($"main img[alt='{alt}']")).ToHaveAttributeAsync("data-media-id", mediaId.ToString());

        // A second dry run finds nothing left on the page.
        await Page.GotoAsync($"{Fixture.BaseUrl}{MoveUrl}");
        await Expect(Page.Locator("h1")).ToHaveTextAsync("Move embedded images");
        await Expect(PageRow(pageId)).ToHaveCountAsync(0);
    }

    [SkippableFact]
    public async Task TheResultPage_HasNoWcagViolations()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Linux), "Playwright browser test Linux-only");
        await SignInAsAdminAsync();
        var dataUri = "data:image/png;base64," + Convert.ToBase64String(MediaFixtures.UniquePng(40, 20));
        var (_, url) = await PublishedRichTextPageAsync($"<p><img src=\"{dataUri}\" alt=\"{_term} dot\"></p>");

        await Page.GotoAsync($"{Fixture.BaseUrl}{MoveUrl}");
        await AxeChecks.ExpectNoViolationsAsync(Page, MoveUrl);
        await Page.Locator("[data-move-embedded-form] button").ClickAsync();
        await Expect(Page.Locator("h1")).ToHaveTextAsync("Embedded images moved");
        await AxeChecks.ExpectNoViolationsAsync(Page, $"{MoveUrl} (result)");

        await Page.GotoAsync($"{Fixture.BaseUrl}{url}");
        var id = await Page.Locator($"main .govuk-body img[alt='{_term} dot']").GetAttributeAsync("data-media-id");
        if (Guid.TryParse(id, out var mediaId)) _images.Add(mediaId);
        Assert.NotEqual(Guid.Empty, mediaId);
    }
}
