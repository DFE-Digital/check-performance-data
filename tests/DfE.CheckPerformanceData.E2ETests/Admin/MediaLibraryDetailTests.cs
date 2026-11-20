using System.Net;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;

namespace DfE.CheckPerformanceData.E2ETests.Admin;

// An image's own page in a browser: editing its details, replacing its file, deleting it, and the
// delete guard for an image that a page uses. Each test uploads its own image with a unique
// e2e-{guid} name and deletes it again at the end, so runs leave nothing behind. Driven as admin
// (see MediaLibraryTests). Linux-only like the other Playwright interaction tests.
[Trait("Category", "FullRegression")]
[Collection("E2E")]
public sealed class MediaLibraryDetailTests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    private const string LibraryUrl = "/admin/media";
    private readonly List<Guid> _images = [];
    private readonly List<Guid> _usedImages = [];
    private readonly List<Guid> _pages = [];

    // Clean up as admin, then put the editor back so the other tests in the collection are not affected.
    public override async Task DisposeAsync()
    {
        try
        {
            await AuthHelpers.ImpersonateAsAdminAsync(Fixture);
            foreach (var id in _usedImages) await MediaSeedHelpers.ClearUsagesAsync(Fixture.SeedClient, id);
            foreach (var id in _images) await MediaSeedHelpers.DeleteAsync(Fixture.SeedClient, id);
            foreach (var page in _pages) await CmsSeedHelpers.TryDeletePageAsync(Fixture.SeedClient, page);
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(Fixture);
            await base.DisposeAsync();
        }
    }

    private async Task<(Guid Id, string Name)> UploadAsAdminAsync(byte[]? png = null)
    {
        var adminCookie = await AuthHelpers.ImpersonateAsAdminAsync(Fixture);
        if (!string.IsNullOrEmpty(adminCookie) && adminCookie.IndexOf('=') is var eq and > 0)
            await Context.AddCookiesAsync([new Microsoft.Playwright.Cookie { Name = adminCookie[..eq], Value = adminCookie[(eq + 1)..], Url = Fixture.BaseUrl }]);

        var name = $"e2e-{Guid.NewGuid():N}-detail.png";
        var id = await MediaSeedHelpers.UploadAsync(Fixture.SeedClient, name, png ?? MediaFixtures.UniquePng());
        _images.Add(id);
        return (id, name);
    }

    private async Task OpenAsync(string path)
    {
        var response = await Page.GotoAsync($"{Fixture.BaseUrl}{path}");
        Assert.Equal(200, response!.Status);
    }

    private ILocator GridItem(string fileName) =>
        Page.Locator("[data-media-library-list] [data-media-item]", new() { HasText = fileName });

    [SkippableFact]
    public async Task EditingTheAltText_RemovesTheNoAltTextTag_FromTheGrid()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Linux), "Playwright interaction test Linux-only");
        var (id, name) = await UploadAsAdminAsync();
        await OpenAsync($"{LibraryUrl}?search={name}");
        await Expect(GridItem(name).Locator("[data-media-missing-alt]")).ToHaveTextAsync("No alt text");

        await OpenAsync($"{LibraryUrl}/{id}");
        await Expect(Page.Locator("h1")).ToHaveTextAsync(name);
        await Expect(Page.Locator("[data-media-detail] [data-media-missing-alt]")).ToBeVisibleAsync();

        // Too long: the page shows again with the GOV.UK error summary and keeps what was typed.
        var alt = Page.GetByLabel("Default alt text");
        await alt.FillAsync(new string('a', 1001));
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save image details" }).ClickAsync();
        await Expect(Page.Locator(".govuk-error-summary")).ToContainTextAsync("Alt text must be 1000 characters or fewer");
        await Expect(Page).ToHaveTitleAsync(new Regex("^Error: "));

        await Page.GetByLabel("Default alt text").FillAsync("Two coloured blocks side by side");
        await Page.GetByLabel("Tags (optional)").FillAsync("e2e, blocks");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save image details" }).ClickAsync();

        await Expect(Page.Locator("[data-media-success]")).ToContainTextAsync("Image details saved");
        await Expect(Page.Locator("[data-media-detail]")).ToContainTextAsync("Two coloured blocks side by side");
        await Expect(Page.GetByLabel("Tags (optional)")).ToHaveValueAsync("e2e, blocks");

        await OpenAsync($"{LibraryUrl}?search={name}");
        await Expect(GridItem(name)).ToBeVisibleAsync();
        await Expect(GridItem(name).Locator("[data-media-missing-alt]")).ToHaveCountAsync(0);
    }

    [SkippableFact]
    public async Task ReplacingTheFile_ShowsVersion2_TheNewDimensions_AndTheHistory()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Linux), "Playwright interaction test Linux-only");
        var (id, _) = await UploadAsAdminAsync(MediaFixtures.UniquePng(48, 24));
        await OpenAsync($"{LibraryUrl}/{id}");
        await Expect(Page.Locator("[data-media-dimensions]")).ToHaveTextAsync("48 × 24 pixels");
        await Expect(Page.Locator("[data-media-copy-link]")).ToBeVisibleAsync();
        await Expect(Page.Locator("#public-url")).ToHaveValueAsync(new Regex($"/media/{id}/1/.+\\.png$"));

        // No file chosen: the error is on the file control and in the summary.
        var upload = Page.GetByRole(AriaRole.Button, new() { Name = "Upload new version" });
        await upload.ClickAsync();
        await Expect(Page.Locator(".govuk-error-summary")).ToContainTextAsync("Select an image to upload");
        await Expect(Page.Locator("[data-media-replace-error]")).ToContainTextAsync("Select an image to upload");

        await Page.Locator("input[name='file']").SetInputFilesAsync(new FilePayload
        {
            Name = "wider.png", MimeType = "image/png", Buffer = MediaFixtures.UniquePng(96, 32)
        });
        await Page.GetByRole(AriaRole.Button, new() { Name = "Upload new version" }).ClickAsync();

        await Expect(Page.Locator("[data-media-success]")).ToContainTextAsync("Version 2 uploaded");
        await Expect(Page.Locator("[data-media-version]")).ToHaveTextAsync("2");
        await Expect(Page.Locator("[data-media-dimensions]")).ToHaveTextAsync("96 × 32 pixels");
        await Expect(Page.Locator("[data-media-versions] tbody tr")).ToHaveCountAsync(2);
        await Expect(Page.Locator("[data-media-version-row='1']")).ToContainTextAsync("48 × 24 pixels");
        await Expect(Page.Locator("[data-media-version-row='2']")).ToContainTextAsync("Current");
        await Expect(Page.Locator("#public-url")).ToHaveValueAsync(new Regex($"/media/{id}/2/.+\\.png$"));
    }

    [SkippableFact]
    public async Task DeletingAnUnusedImage_RemovesItFromTheGrid()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Linux), "Playwright interaction test Linux-only");
        var (id, name) = await UploadAsAdminAsync();
        await OpenAsync($"{LibraryUrl}/{id}");

        await Page.GetByRole(AriaRole.Button, new() { Name = $"Delete image {name}" }).ClickAsync();
        await Expect(Page.Locator("h1")).ToHaveTextAsync("Delete image");
        await Expect(Page.Locator(".govuk-warning-text")).ToContainTextAsync("This cannot be undone.");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Delete image" }).ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex("/admin/media$"));
        await Expect(Page.Locator("[data-media-success]")).ToContainTextAsync($"{name} deleted");
        await OpenAsync($"{LibraryUrl}?search={name}");
        await Expect(Page.Locator("[data-media-library-empty]")).ToBeVisibleAsync();
        await Expect(GridItem(name)).ToHaveCountAsync(0);
        var gone = await Page.GotoAsync($"{Fixture.BaseUrl}{LibraryUrl}/{id}");
        Assert.Equal(404, gone!.Status);
    }

    // Pages do not record their images yet, so a dev endpoint records the use directly.
    [SkippableFact]
    public async Task AUsedImage_ListsThePagesThatUseIt_AndCannotBeDeleted()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Linux), "Playwright interaction test Linux-only");
        var (id, _) = await UploadAsAdminAsync();
        var title = $"E2E media user {Guid.NewGuid():N}";
        var pageId = await CmsSeedHelpers.CreatePageNodeAsync(Fixture.SeedClient, FixtureContent.RootId, "content",
            $"e2e-{Guid.NewGuid():N}-media-user", title);
        _pages.Add(pageId);
        await CmsSeedHelpers.SaveDraftAsync(Fixture.SeedClient, pageId);
        await MediaSeedHelpers.AddUsageAsync(Fixture.SeedClient, id, pageId);
        _usedImages.Add(id);

        await OpenAsync($"{LibraryUrl}/{id}");
        await Expect(Page.Locator("[data-media-used-on]")).ToHaveTextAsync("1 page");

        await OpenAsync($"{LibraryUrl}/{id}/delete");
        await Expect(Page.Locator("[data-media-in-use]")).ToContainTextAsync("This image is used on 1 page");
        await Expect(Page.Locator("[data-media-usages] a", new() { HasText = title })).ToHaveAttributeAsync("href", $"/admin/pages/{pageId}");
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Delete image" })).ToHaveCountAsync(0);

        // A post made anyway is refused, and the image stays.
        Assert.Equal(HttpStatusCode.Conflict, await MediaSeedHelpers.DeleteAsync(Fixture.SeedClient, id));
        await OpenAsync($"{LibraryUrl}/{id}");
    }

    [SkippableFact]
    public async Task TheImagePage_AndTheDeletePage_HaveNoWcagViolations()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Linux), "Playwright browser test Linux-only");
        var (id, _) = await UploadAsAdminAsync();

        foreach (var path in new[] { $"{LibraryUrl}/{id}", $"{LibraryUrl}/{id}/delete" })
        {
            await OpenAsync(path);
            await AxeChecks.ExpectNoViolationsAsync(Page, path);
        }
    }
}
