using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;

namespace DfE.CheckPerformanceData.E2ETests.Admin;

// The media library page's width and its three views, in a browser: the full-width layout puts
// more than three images in a row, and the View select shows the grid, a details table or one
// large image to a row. The view stays in the address, and the search and filters keep it.
// Each run uploads five images named e2e-{guid}-..., finds them by that name, and deletes them.
[Trait("Category", "FullRegression")]
[Collection("E2E")]
public sealed class MediaLibraryViewTests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    private readonly string _term = $"e2e-{Guid.NewGuid():N}";
    private readonly List<Guid> _images = [];

    public override async Task DisposeAsync()
    {
        try
        {
            await AuthHelpers.ImpersonateAsAdminAsync(Fixture);
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

    private async Task UploadImagesAsync(int count)
    {
        for (var i = 0; i < count; i++)
        {
            _images.Add(await MediaSeedHelpers.UploadAsync(Fixture.SeedClient, $"{_term}-{i}.png", MediaFixtures.UniquePng(60 + i, 40)));
        }
    }

    private ILocator ViewSelect => Page.GetByLabel("View", new() { Exact = true });

    [Fact]
    public async Task TheGrid_UsesTheFullWidth_AndShowsMoreThanThreeImagesInARow()
    {
        await SignInAsAdminAsync();
        await UploadImagesAsync(5);
        await Page.SetViewportSizeAsync(1280, 900);

        await Page.GotoAsync($"{Fixture.BaseUrl}/admin/media?search={_term}");

        var items = Page.Locator("[data-media-library-list] [data-media-item]");
        await Expect(items).ToHaveCountAsync(5);
        var tops = await items.EvaluateAllAsync<double[]>("els => els.map(e => e.getBoundingClientRect().top)");
        var inFirstRow = tops.Count(t => Math.Abs(t - tops[0]) < 1);
        Assert.True(inFirstRow > 3, $"The first row holds {inFirstRow} images.");
    }

    [Fact]
    public async Task ChoosingDetails_ShowsATableOfFacts_AndTheSearchAndFiltersKeepIt()
    {
        await SignInAsAdminAsync();
        await UploadImagesAsync(2);
        await Page.GotoAsync($"{Fixture.BaseUrl}/admin/media?search={_term}&sort=name");
        await Expect(ViewSelect).ToHaveValueAsync("grid");

        // site.js submits the form when the select changes; there is no button to press.
        await ViewSelect.SelectOptionAsync("details");
        await Page.WaitForURLAsync(url => url.Contains("view=details"));
        Assert.Contains($"search={_term}", Page.Url);
        Assert.Contains("sort=name", Page.Url);

        var table = Page.Locator("[data-media-library-table]");
        await Expect(table.Locator("caption")).ToHaveTextAsync("Images");
        var row = table.Locator($"tr[data-media-item='{_images[0]:D}']");
        await Expect(row.GetByRole(AriaRole.Link, new() { Name = $"{_term}-0.png" })).ToBeVisibleAsync();
        var facts = row.Locator("[data-media-facts]");
        await Expect(facts).ToContainTextAsync("PNG");
        await Expect(facts).ToContainTextAsync("60 × 40 pixels");
        await Expect(facts).ToContainTextAsync("No alt text");
        await Expect(row.Locator("[data-media-uses]")).ToHaveTextAsync("Not used");
        await Expect(facts).ToContainTextAsync("Uploaded");
        await Expect(row.Locator("img")).ToHaveAttributeAsync("alt", "");

        // The filter form keeps the view.
        await Page.GetByLabel("Not used on any page").CheckAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Apply" }).ClickAsync();
        await Page.WaitForURLAsync(url => url.Contains("unused=true"));
        Assert.Contains("view=details", Page.Url);
        await Expect(ViewSelect).ToHaveValueAsync("details");
        await Expect(table.Locator("tbody tr")).ToHaveCountAsync(2);

        // So does "Clear search and filters".
        await Page.GetByRole(AriaRole.Link, new() { Name = "Clear search and filters" }).ClickAsync();
        await Page.WaitForURLAsync(url => !url.Contains("unused="));
        Assert.EndsWith("/admin/media?view=details", Page.Url);
    }

    [Fact]
    public async Task ChoosingLarge_ShowsOneLargeImageInEachRow()
    {
        await SignInAsAdminAsync();
        await UploadImagesAsync(2);
        await Page.SetViewportSizeAsync(1280, 900);
        await Page.GotoAsync($"{Fixture.BaseUrl}/admin/media?search={_term}");

        await ViewSelect.SelectOptionAsync("large");
        await Page.WaitForURLAsync(url => url.Contains("view=large"));

        var items = Page.Locator("[data-media-library-large] [data-media-item]");
        await Expect(items).ToHaveCountAsync(2);
        var lefts = await items.EvaluateAllAsync<double[]>("els => els.map(e => e.getBoundingClientRect().left)");
        var tops = await items.EvaluateAllAsync<double[]>("els => els.map(e => e.getBoundingClientRect().top)");
        Assert.Equal(lefts[0], lefts[1], 1);
        Assert.True(tops[1] > tops[0], "The second image is below the first.");
        var img = items.First.Locator("img");
        await Expect(img).ToHaveAttributeAsync("src", new System.Text.RegularExpressions.Regex("sig="));
        await Expect(items.First.Locator("[data-media-facts]")).ToContainTextAsync("PNG");
    }
}
