using System.Runtime.InteropServices;
using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;

namespace DfE.CheckPerformanceData.E2ETests.Admin;

// The media library page in a browser: uploading through the form (with and without
// JavaScript), the grid, search, the "Missing alt text" filter, the duplicate prompt and the
// error for a file that is not an image. Each run uploads files no earlier run has made, because
// the library refuses a second copy. Each upload is named e2e-{guid}-..., and the test deletes
// those images again at the end. Linux-only like the other Playwright interaction tests.
[Trait("Category", "FullRegression")]
[Collection("E2E")]
public sealed class MediaLibraryUploadTests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    private const string LibraryUrl = "/admin/media";

    // The unique e2e-{guid} names this test uploaded, deleted again in DisposeAsync.
    private readonly List<string> _uploaded = [];

    private string Track(string name)
    {
        _uploaded.Add(name);
        return name;
    }

    // Deletes this test's images as admin. ImpersonateAsAdminAsync makes admin the fixture-wide
    // default; put the editor back so the other tests in the collection are not affected.
    public override async Task DisposeAsync()
    {
        try
        {
            await AuthHelpers.ImpersonateAsAdminAsync(Fixture);
            foreach (var name in _uploaded) await MediaSeedHelpers.DeleteMatchingAsync(Fixture.SeedClient, name);
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(Fixture);
            await base.DisposeAsync();
        }
    }

    private static async Task SignInAsAdminAsync(PlaywrightFixture fixture, IBrowserContext context)
    {
        var adminCookie = await AuthHelpers.ImpersonateAsAdminAsync(fixture);
        if (string.IsNullOrEmpty(adminCookie)) return;
        var eq = adminCookie.IndexOf('=');
        if (eq <= 0) return;
        await context.AddCookiesAsync([new Cookie { Name = adminCookie[..eq], Value = adminCookie[(eq + 1)..], Url = fixture.BaseUrl }]);
    }

    private async Task OpenLibraryAsync(string query = "")
    {
        await Page.GotoAsync($"{Fixture.BaseUrl}{LibraryUrl}{query}");
        await Expect(Page.Locator("html")).ToHaveAttributeAsync("data-media-upload-init", "true");
    }

    private async Task UploadAsync(params FilePayload[] files)
    {
        await Page.Locator("input[name='files']").SetInputFilesAsync(files);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Upload", Exact = true }).ClickAsync();
        await Expect(Page.Locator("[data-media-upload-status]")).ToContainTextAsync("Finished.", new() { Timeout = 60_000 });
    }

    private static FilePayload Png(string name, byte[]? bytes = null) =>
        new() { Name = name, MimeType = "image/png", Buffer = bytes ?? MediaFixtures.UniquePng() };

    private ILocator GridItem(string fileName) =>
        Page.Locator("[data-media-library-list] [data-media-item]", new() { HasText = fileName });

    [SkippableFact]
    public async Task UploadingAPngAndAnSvg_ShowsBothInTheGrid_AndSearchFindsOne()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Linux), "Playwright interaction test Linux-only");
        var run = Track($"e2e-{Guid.NewGuid():N}");
        var png = $"{run}-chart.png";
        var svg = $"{run}-logo.svg";
        await SignInAsAdminAsync(Fixture, Context);
        await OpenLibraryAsync();

        await UploadAsync(Png(png), new FilePayload { Name = svg, MimeType = "image/svg+xml", Buffer = MediaFixtures.UniqueSvg() });

        var uploaded = Page.Locator("[data-media-upload-result='created']");
        await Expect(uploaded).ToHaveCountAsync(2);
        await Expect(uploaded.GetByRole(AriaRole.Link, new() { Name = png })).ToHaveAttributeAsync("href", new System.Text.RegularExpressions.Regex("^/admin/media/[0-9a-f-]{36}$"));
        // The grid reloads after the uploads, newest first, so both are on the first page.
        await Expect(GridItem(png)).ToBeVisibleAsync();
        await Expect(GridItem(svg)).ToBeVisibleAsync();
        await Expect(GridItem(png).Locator("img")).ToHaveAttributeAsync("src", new System.Text.RegularExpressions.Regex(@"\.webp\?.*w=320.*sig="));
        await Expect(GridItem(svg).Locator("img")).ToHaveAttributeAsync("src", new System.Text.RegularExpressions.Regex(@"\.svg$"));
        await Expect(GridItem(png)).ToContainTextAsync("48 × 24 pixels");
        await Expect(GridItem(png).Locator("[data-media-missing-alt]")).ToHaveTextAsync("No alt text");

        await Page.Locator("#search").FillAsync($"{run}-chart");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Apply" }).ClickAsync();
        await Expect(Page.Locator("[data-media-library-list] [data-media-item]")).ToHaveCountAsync(1);
        await Expect(GridItem(png)).ToBeVisibleAsync();

        // The thumbnail opens the image's own page.
        await GridItem(png).GetByRole(AriaRole.Link).ClickAsync();
        await Expect(Page.Locator("h1")).ToHaveTextAsync(png);
    }

    [SkippableFact]
    public async Task TheMissingAltTextFilter_LeavesOutImagesWithAltText()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Linux), "Playwright interaction test Linux-only");
        await MediaSeedHelpers.SeedSampleAsync(Fixture.SeedClient, "svg", alt: "Green and blue blocks");
        var png = Track($"e2e-{Guid.NewGuid():N}-no-alt.png");
        await SignInAsAdminAsync(Fixture, Context);
        await OpenLibraryAsync();
        await UploadAsync(Png(png));

        await OpenLibraryAsync("?search=sample.svg");
        await Expect(GridItem("sample.svg")).ToBeVisibleAsync();

        await Page.GetByLabel("Missing alt text").CheckAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Apply" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("missingAlt=true"));
        await Expect(GridItem("sample.svg")).ToHaveCountAsync(0);
        await Expect(Page.Locator("[data-media-library-empty]")).ToHaveTextAsync("No images match your search and filters.");

        await OpenLibraryAsync($"?search={png}&missingAlt=true");
        await Expect(GridItem(png)).ToBeVisibleAsync();
    }

    [SkippableFact]
    public async Task UploadingTheSameImageAgain_OffersTheExistingImage()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Linux), "Playwright interaction test Linux-only");
        var bytes = MediaFixtures.UniquePng();
        var first = Track($"e2e-{Guid.NewGuid():N}-first.png");
        var again = $"e2e-{Guid.NewGuid():N}-again.png";
        await SignInAsAdminAsync(Fixture, Context);
        await OpenLibraryAsync();
        await UploadAsync(Png(first, bytes));

        await UploadAsync(Png(again, bytes));

        var duplicate = Page.Locator("[data-media-upload-result='duplicate']");
        await Expect(duplicate).ToContainTextAsync($"This image is already in the library as {first}.");
        await duplicate.GetByRole(AriaRole.Link, new() { Name = "Use the existing image" }).ClickAsync();
        await Expect(Page.Locator("h1")).ToHaveTextAsync(first);
    }

    [SkippableFact]
    public async Task AFileThatIsNotAnImage_IsRefused_WithTheReasonInTheErrorSummary()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Linux), "Playwright interaction test Linux-only");
        await SignInAsAdminAsync(Fixture, Context);
        await OpenLibraryAsync();

        await UploadAsync(new FilePayload { Name = "notes.txt", MimeType = "text/plain", Buffer = "Not an image"u8.ToArray() });

        var summary = Page.Locator("[data-media-upload-errors]");
        await Expect(summary).ToBeVisibleAsync();
        await Expect(summary).ToBeFocusedAsync();
        await Expect(summary).ToContainTextAsync("notes.txt: The selected file must be a PNG, JPEG, GIF, WebP or SVG image.");
        await Expect(Page.Locator("[data-media-upload-result='rejected']")).ToContainTextAsync("Not uploaded");
    }

    [SkippableFact]
    public async Task WithoutJavaScript_ThePlainFormUploads_AndShowsTheResults()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Linux), "Playwright interaction test Linux-only");
        var png = Track($"e2e-{Guid.NewGuid():N}-plain.png");
        await using var context = await Browser.NewContextAsync(new() { JavaScriptEnabled = false });
        await SignInAsAdminAsync(Fixture, context);
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{Fixture.BaseUrl}{LibraryUrl}");

        await page.Locator("input[name='files']").SetInputFilesAsync(new[]
        {
            Png(png),
            new FilePayload { Name = "notes.txt", MimeType = "text/plain", Buffer = "Not an image"u8.ToArray() },
        });
        await page.GetByRole(AriaRole.Button, new() { Name = "Upload", Exact = true }).ClickAsync();

        await Expect(page.Locator("[data-media-upload-result='created']")).ToContainTextAsync(png);
        await Expect(page.Locator(".govuk-error-summary")).ToContainTextAsync("notes.txt: The selected file must be a PNG, JPEG, GIF, WebP or SVG image.");
        await Expect(page).ToHaveTitleAsync(new System.Text.RegularExpressions.Regex("^Error: Media library"));
        await Expect(page.Locator("[data-media-library-list] [data-media-item]", new() { HasText = png })).ToBeVisibleAsync();
    }
}
