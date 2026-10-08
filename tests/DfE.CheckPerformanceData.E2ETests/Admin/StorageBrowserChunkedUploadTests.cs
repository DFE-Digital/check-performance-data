using System.Runtime.InteropServices;
using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;

namespace DfE.CheckPerformanceData.E2ETests.Admin;

// #568: the storage browser's upload-in-parts enhancement. Linux-only [SkippableFact] like
// the other Playwright-interaction tests. Runs against the compose stack, where the app
// account's rules-config container exists.
[Collection("E2E")]
public sealed class StorageBrowserChunkedUploadTests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    private const string ContainerUrl = "/admin/storage/app/rules-config";

    private async Task SignInAsAdminInBrowserAsync()
    {
        var adminCookie = await AuthHelpers.ImpersonateAsAdminAsync(Fixture);
        if (string.IsNullOrEmpty(adminCookie)) return;
        var equalsIndex = adminCookie.IndexOf('=');
        if (equalsIndex <= 0) return;
        await Context.AddCookiesAsync([new Cookie
        {
            Name = adminCookie[..equalsIndex],
            Value = adminCookie[(equalsIndex + 1)..],
            Url = Fixture.BaseUrl
        }]);
    }

    private static byte[] Bytes(int length)
    {
        var bytes = new byte[length];
        for (var i = 0; i < length; i++) bytes[i] = (byte)('a' + (i % 26));
        return bytes;
    }

    private async Task ChooseAndUploadAsync(string folder, string fileName, int length)
    {
        await Page.Locator("#folder").FillAsync(folder);
        await Page.Locator("#files").SetInputFilesAsync(new FilePayload { Name = fileName, MimeType = "text/csv", Buffer = Bytes(length) });
        await Page.GetByRole(AriaRole.Button, new() { Name = "Upload", Exact = true }).ClickAsync();
    }

    private async Task DeleteAsync(string fileName)
    {
        Page.Dialog += (_, dialog) => dialog.AcceptAsync();
        var row = Page.Locator("tr", new() { HasText = fileName });
        await row.GetByRole(AriaRole.Button, new() { Name = "Delete" }).ClickAsync();
        await Expect(Page.Locator("tr", new() { HasText = fileName })).ToHaveCountAsync(0);
    }

    // Runs in a finally block: removes <folder>/<fileName> through the app's own delete form if it
    // exists, and never throws, so it cannot mask the assertion failure that got us here.
    private static async Task CleanUpAsync(IBrowserContext context, string baseUrl, string folder)
    {
        try
        {
            var page = await context.NewPageAsync();
            page.Dialog += (_, dialog) => dialog.AcceptAsync();
            await page.GotoAsync($"{baseUrl}{ContainerUrl}?prefix={folder}%2F");
            for (var rows = page.Locator("tbody tr"); await rows.CountAsync() > 0;)
            {
                await rows.First.GetByRole(AriaRole.Button, new() { Name = "Delete" }).ClickAsync();
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                await page.GotoAsync($"{baseUrl}{ContainerUrl}?prefix={folder}%2F");
            }
            await page.CloseAsync();
        }
        catch (Exception)
        {
            // Best effort only.
        }
    }

    // 20 MB at 8 MiB parts is three parts: progress is announced, the folder reloads with the file.
    [SkippableFact]
    public async Task ALargeFile_IsUploadedInParts_AndAppearsInTheFolder()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Linux), "Playwright interaction test Linux-only");
        var folder = $"e2e-{Guid.NewGuid():N}";
        var fileName = "big.csv";
        await SignInAsAdminInBrowserAsync();
        await Page.GotoAsync($"{Fixture.BaseUrl}{ContainerUrl}");
        await Expect(Page.Locator("#storage-upload-form")).ToHaveAttributeAsync("data-chunk-bytes", "8388608");
        await Expect(Page.Locator("#files-hint")).ToContainTextAsync("Each file must be smaller than");

        var parts = new List<string>();
        await Page.RouteAsync("**/upload/block*", async route =>
        {
            parts.Add(new Uri(route.Request.Url).Query);
            await route.ContinueAsync();
        });

        try
        {
            await ChooseAndUploadAsync(folder, fileName, 20 * 1024 * 1024);

            await Page.WaitForURLAsync($"**{ContainerUrl}?prefix={folder}%2F");
            await Expect(Page.Locator("tr", new() { HasText = fileName })).ToContainTextAsync("20.0 MB");
            Assert.Equal(3, parts.Count);
            Assert.All(parts, q => Assert.Contains("fileName=big.csv", q));

            await DeleteAsync(fileName);
        }
        finally
        {
            await CleanUpAsync(Context, Fixture.BaseUrl, folder);
        }
    }

    [SkippableFact]
    public async Task WithoutJavaScript_ThePlainFormStillUploads()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Linux), "Playwright interaction test Linux-only");
        var folder = $"e2e-{Guid.NewGuid():N}";
        var fileName = "small.csv";
        var adminCookie = await AuthHelpers.ImpersonateAsAdminAsync(Fixture);
        await using var context = await Browser.NewContextAsync(new() { JavaScriptEnabled = false });
        if (!string.IsNullOrEmpty(adminCookie))
        {
            var eq = adminCookie.IndexOf('=');
            await context.AddCookiesAsync([new Cookie { Name = adminCookie[..eq], Value = adminCookie[(eq + 1)..], Url = Fixture.BaseUrl }]);
        }
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{Fixture.BaseUrl}{ContainerUrl}");
        await Expect(page.Locator("#files-hint")).ToContainTextAsync("must add up to less than 50MB");
        // GetByText skips <noscript> content in a JavaScript-disabled context, so assert on the element.
        await Expect(page.Locator("noscript p")).ToContainTextAsync("To upload bigger files, turn on JavaScript in your browser.");

        try
        {
            await page.Locator("#folder").FillAsync(folder);
            await page.Locator("#files").SetInputFilesAsync(new FilePayload { Name = fileName, MimeType = "text/csv", Buffer = Bytes(1024) });
            await page.GetByRole(AriaRole.Button, new() { Name = "Upload", Exact = true }).ClickAsync();

            await page.WaitForURLAsync($"**{ContainerUrl}");
            await page.GotoAsync($"{Fixture.BaseUrl}{ContainerUrl}?prefix={folder}%2F");
            await Expect(page.Locator("tr", new() { HasText = fileName })).ToBeVisibleAsync();

            // No confirm dialog here: the onclick confirm needs JavaScript, which is off.
            await page.Locator("tr", new() { HasText = fileName }).GetByRole(AriaRole.Button, new() { Name = "Delete" }).ClickAsync();
            await Expect(page.Locator("tr", new() { HasText = fileName })).ToHaveCountAsync(0);
        }
        finally
        {
            await CleanUpAsync(context, Fixture.BaseUrl, folder);
        }
    }

    // The ingress controller answers 413 itself in deployed environments; compose has no
    // ingress, so the response is faked at the route. The user must get a GOV.UK error with
    // focus on it, not a silent stall.
    [SkippableFact]
    public async Task APartRefusedWith413_ShowsAnErrorSummary_WithFocus()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Linux), "Playwright interaction test Linux-only");
        await SignInAsAdminInBrowserAsync();
        await Page.GotoAsync($"{Fixture.BaseUrl}{ContainerUrl}");
        await Page.RouteAsync("**/upload/block*", route => route.FulfillAsync(new() { Status = 413, Body = "" }));

        await ChooseAndUploadAsync($"e2e-{Guid.NewGuid():N}", "refused.csv", 1024);

        var summary = Page.Locator("#upload-error-summary");
        await Expect(summary).ToBeVisibleAsync();
        await Expect(summary).ToContainTextAsync("The upload did not complete. Upload the file again.");
        await Expect(summary).ToBeFocusedAsync();
        await Expect(Page.Locator("#files-error")).ToContainTextAsync("Error:");
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Upload", Exact = true })).ToBeEnabledAsync();
    }

    // The script reads data-max-bytes at submit time, so the test can shrink the client-side
    // maximum and does not need a 2 GB file.
    [SkippableFact]
    public async Task AFileOverTheMaximum_IsRefusedBeforeAnyPartIsSent()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Linux), "Playwright interaction test Linux-only");
        await SignInAsAdminInBrowserAsync();
        await Page.GotoAsync($"{Fixture.BaseUrl}{ContainerUrl}");
        await Page.EvaluateAsync("document.getElementById('storage-upload-form').setAttribute('data-max-bytes', '512')");
        var sent = 0;
        await Page.RouteAsync("**/upload/block*", route => { sent++; return route.ContinueAsync(); });

        var folder = $"e2e-{Guid.NewGuid():N}";
        try
        {
            await ChooseAndUploadAsync(folder, "huge.csv", 4096);

            await Expect(Page.Locator("#upload-error-summary")).ToContainTextAsync("The selected file must be smaller than");
            await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            Assert.Equal(0, sent);
        }
        finally
        {
            await CleanUpAsync(Context, Fixture.BaseUrl, folder);
        }
    }
}
