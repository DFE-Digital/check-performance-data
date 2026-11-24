using System.Net;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;

namespace DfE.CheckPerformanceData.E2ETests.Admin;

// The media library reports in a browser. The alt text report lists an image with no alt text and
// a page that gives it a weak alt text, and each row links to where it is fixed. The size report
// lists a large image among the largest. Each test uploads its own e2e-{guid} image and deletes it
// again. Both reports list the most recent first or the largest first, so the image is on the
// first page of a shared stack. Driven as admin (see MediaLibraryTests). Linux-only like the other
// Playwright interaction tests.
[Trait("Category", "FullRegression")]
[Collection("E2E")]
public sealed class MediaReportsTests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    private const string AltTextUrl = "/admin/media/reports/alt-text";
    private const string SizeUrl = "/admin/media/reports/size";
    private readonly List<Guid> _images = [];
    private readonly List<Guid> _pages = [];

    public override async Task DisposeAsync()
    {
        try
        {
            await AuthHelpers.ImpersonateAsAdminAsync(Fixture);
            foreach (var id in _images) await MediaSeedHelpers.ClearUsagesAsync(Fixture.SeedClient, id);
            foreach (var id in _images) await MediaSeedHelpers.DeleteAsync(Fixture.SeedClient, id);
            foreach (var page in _pages) await CmsSeedHelpers.TryDeletePageAsync(Fixture.SeedClient, page);
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(Fixture);
            await base.DisposeAsync();
        }
    }

    private async Task<Guid> UploadAsAdminAsync(byte[] png)
    {
        var adminCookie = await AuthHelpers.ImpersonateAsAdminAsync(Fixture);
        if (!string.IsNullOrEmpty(adminCookie) && adminCookie.IndexOf('=') is var eq and > 0)
            await Context.AddCookiesAsync([new Microsoft.Playwright.Cookie { Name = adminCookie[..eq], Value = adminCookie[(eq + 1)..], Url = Fixture.BaseUrl }]);

        var id = await MediaSeedHelpers.UploadAsync(Fixture.SeedClient, $"e2e-{Guid.NewGuid():N}-report.png", png);
        _images.Add(id);
        return id;
    }

    // A published page with an Image widget whose alt text is a general word on its own.
    private async Task<Guid> PublishPageWithWeakAltAsync(Guid mediaId, string title)
    {
        var id = await CmsSeedHelpers.CreatePageNodeAsync(
            Fixture.SeedClient, FixtureContent.RootId, "content", $"e2e-report-{Guid.NewGuid():N}", title);
        _pages.Add(id);
        await CmsSeedHelpers.AddWidgetAsync(Fixture.SeedClient, id, "0.0", "image");
        await CmsSeedHelpers.UpdateWidgetAsync(Fixture.SeedClient, id, "0.0", "image", new Dictionary<string, string>
        {
            ["mediaId"] = mediaId.ToString(),
            ["alt"] = "photo",
            ["decorative"] = "false",
            ["caption"] = "",
            ["credit"] = "",
            ["width"] = "full",
            ["align"] = "",
            ["href"] = "",
            ["crop"] = "",
            ["aspect"] = "",
            ["focal"] = "",
        });
        await CmsSeedHelpers.PublishDraftAsync(Fixture.SeedClient, id);
        return id;
    }

    private async Task OpenAsync(string path)
    {
        var response = await Page.GotoAsync($"{Fixture.BaseUrl}{path}");
        Assert.Equal(200, response!.Status);
    }

    [SkippableFact]
    public async Task TheAltTextReport_ListsTheImageAndTheWeakUse_AndLinksToWhereEachIsFixed()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Linux), "Playwright interaction test Linux-only");
        var id = await UploadAsAdminAsync(MediaFixtures.UniquePng());
        var title = $"E2E alt report {Guid.NewGuid():N}";
        var pageId = await PublishPageWithWeakAltAsync(id, title);

        await OpenAsync("/admin/media");
        await Page.Locator("[data-media-alt-text-report-link]").ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex($"{Regex.Escape(AltTextUrl)}$"));
        await Expect(Page.Locator("h1")).ToHaveTextAsync("Alt text report");

        var imageRow = Page.Locator($"[data-alt-report-image='{id}']");
        await Expect(imageRow).ToContainTextAsync("No alt text");

        var useRow = Page.Locator($"[data-alt-report-use='{id}']", new() { HasText = title });
        await Expect(useRow).ToContainTextAsync("photo");
        await Expect(useRow).ToContainTextAsync("Only a general word");
        await Expect(useRow).ToContainTextAsync("Live");

        await useRow.Locator("[data-alt-report-fix]").ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex($"/admin/pages/{pageId}$"));

        await OpenAsync(AltTextUrl);
        await imageRow.GetByRole(AriaRole.Link).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex($"/admin/media/{id}$"));
    }

    [SkippableFact]
    public async Task TheSizeReport_ListsALargeImage_AmongTheLargest()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Linux), "Playwright interaction test Linux-only");
        var id = await UploadAsAdminAsync(MediaFixtures.NoisyPng(700, 700));

        await OpenAsync("/admin/media");
        await Page.Locator("[data-media-size-report-link]").ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex($"{Regex.Escape(SizeUrl)}$"));
        await Expect(Page.Locator("h1")).ToHaveTextAsync("Size report");
        await Expect(Page.Locator("[data-size-report-library]")).ToBeVisibleAsync();
        await Expect(Page.Locator("[data-size-report-renditions]")).ToBeVisibleAsync();

        var row = Page.Locator($"[data-size-report-row='{id}']");
        await Expect(row).ToContainTextAsync("MB");
        await row.GetByRole(AriaRole.Link).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex($"/admin/media/{id}$"));
    }

    // A signed-in user with no media grant gets 404 for both reports, as for the library itself.
    [Theory]
    [InlineData(AltTextUrl)]
    [InlineData(SizeUrl)]
    public async Task UserWithoutTheMediaSection_GetsNotFound(string path)
    {
        try
        {
            await AuthHelpers.ImpersonateAsUnprivilegedUserAsync(Fixture);

            using var response = await Fixture.SeedClient.SendAsync(new HttpRequestMessage(HttpMethod.Get, $"{Fixture.BaseUrl}{path}"));

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(Fixture);
        }
    }
}
