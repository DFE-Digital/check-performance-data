using System.Net;
using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;

namespace DfE.CheckPerformanceData.E2ETests.Admin;

// "Delete unused images" in a browser, as the dev admin. The library search scopes the run to
// this test's own e2e-{guid} images, so no other test's images are touched. The confirmation page
// lists only unused images. An image that a page starts to use after that page is shown is not
// deleted: the delete checks each image's uses again.
[Trait("Category", "FullRegression")]
[Collection("E2E")]
public sealed class MediaDeleteUnusedTests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    private readonly List<Guid> _pages = [];
    private readonly List<Guid> _images = [];
    private readonly string _run = $"e2e-unused-{Guid.NewGuid():N}";

    public override async Task DisposeAsync()
    {
        try
        {
            await AuthHelpers.ImpersonateAsAdminAsync(Fixture);
            foreach (var id in _images) await MediaSeedHelpers.ClearUsagesAsync(Fixture.SeedClient, id);
            foreach (var id in _images) await MediaSeedHelpers.DeleteAsync(Fixture.SeedClient, id);
            foreach (var id in _pages) await CmsSeedHelpers.TryDeletePageAsync(Fixture.SeedClient, id);
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(Fixture);
            await base.DisposeAsync();
        }
    }

    private async Task<Guid> UploadAsync(string name)
    {
        var id = await MediaSeedHelpers.UploadAsync(Fixture.SeedClient, $"{_run}-{name}.png", MediaFixtures.UniquePng());
        _images.Add(id);
        return id;
    }

    private async Task<HttpStatusCode> StatusAsync(string path)
    {
        using var response = await Fixture.SeedClient.SendAsync(new HttpRequestMessage(HttpMethod.Get, $"{Fixture.BaseUrl}{path}"));
        return response.StatusCode;
    }

    [Fact]
    public async Task UnusedImages_AreDeletedAfterAConfirmation_AndAnImageUsedSinceThenStays()
    {
        var adminCookie = await AuthHelpers.ImpersonateAsAdminAsync(Fixture);
        if (!string.IsNullOrEmpty(adminCookie) && adminCookie.IndexOf('=') is var eq and > 0)
            await Context.AddCookiesAsync([new Microsoft.Playwright.Cookie { Name = adminCookie[..eq], Value = adminCookie[(eq + 1)..], Url = Fixture.BaseUrl }]);

        var unused = await UploadAsync("a");
        var used = await UploadAsync("b");
        var usedLater = await UploadAsync("c");
        var page = await CmsSeedHelpers.CreatePageNodeAsync(Fixture.SeedClient, FixtureContent.RootId, "content", _run, "E2E unused images");
        _pages.Add(page);
        await MediaSeedHelpers.AddUsageAsync(Fixture.SeedClient, used, page);

        await Page.GotoAsync($"{Fixture.BaseUrl}/admin/media?search={_run}&unused=true");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Delete unused images" }).ClickAsync();

        await Expect(Page.Locator("h1")).ToHaveTextAsync("Delete unused images");
        var list = Page.Locator("[data-media-delete-unused-list]");
        await Expect(list.Locator($"[data-media-item='{unused}']")).ToBeVisibleAsync();
        await Expect(list.Locator($"[data-media-item='{usedLater}']")).ToBeVisibleAsync();
        await Expect(list.Locator($"[data-media-item='{used}']")).ToHaveCountAsync(0);
        await Expect(list.Locator("tbody tr")).ToHaveCountAsync(2);

        // A page starts to use one of the listed images before the editor confirms.
        await MediaSeedHelpers.AddUsageAsync(Fixture.SeedClient, usedLater, page);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Delete 2 images" }).ClickAsync();

        await Expect(Page.Locator("[data-media-success]"))
            .ToContainTextAsync("1 unused image deleted. 1 image was not deleted, because content now uses it.");
        Assert.Contains("unused=true", Page.Url);
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync($"/admin/media/{unused}"));
        Assert.Equal(HttpStatusCode.OK, await StatusAsync($"/admin/media/{used}"));
        Assert.Equal(HttpStatusCode.OK, await StatusAsync($"/admin/media/{usedLater}"));
    }

    // The delete needs the antiforgery token, like every change in the library.
    [Fact]
    public async Task ThePostWithoutAToken_IsRefused_AndDeletesNothing()
    {
        await AuthHelpers.ImpersonateAsAdminAsync(Fixture);
        var unused = await UploadAsync("token");
        // The admin may open the page, so a refusal below comes from the missing token.
        Assert.Equal(HttpStatusCode.OK, await StatusAsync($"/admin/media/delete-unused?search={_run}"));
        var (_, cookie) = await AntiforgeryHelpers.ScrapeAsync(Fixture.SeedClient, "/dev/antiforgery-token");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{Fixture.BaseUrl}/admin/media/delete-unused")
        {
            Content = new FormUrlEncodedContent([new("ids", unused.ToString())]),
        };
        request.Headers.Add("Cookie", $"{Fixture.SeedClient.ImpersonationCookieHeader}; {cookie}");
        using var response = await Fixture.SeedClient.SendAsync(request);

        Assert.True(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound,
            $"Expected the CSRF guard to refuse the delete; got {(int)response.StatusCode}.");
        Assert.Equal(HttpStatusCode.OK, await StatusAsync($"/admin/media/{unused}"));
    }

    [Fact]
    public async Task AUserWithoutTheMediaSection_GetsNotFound()
    {
        try
        {
            await AuthHelpers.ImpersonateAsUnprivilegedUserAsync(Fixture);

            Assert.Equal(HttpStatusCode.NotFound, await StatusAsync("/admin/media/delete-unused"));
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(Fixture);
        }
    }
}
