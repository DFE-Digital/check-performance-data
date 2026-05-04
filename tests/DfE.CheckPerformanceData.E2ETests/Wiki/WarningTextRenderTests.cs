using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;

namespace DfE.CheckPerformanceData.E2ETests.Wiki;

[Collection("E2E")]
[Trait("Category", "W2")]
public sealed class WarningTextRenderTests(PlaywrightFixture fixture) : PageTest, IAsyncLifetime
{
    private const string WarningTextBody = """
        <div class="govuk-warning-text">
          <span class="govuk-warning-text__icon" aria-hidden="true">!</span>
          <strong class="govuk-warning-text__text">
            <span class="govuk-visually-hidden">Warning</span>
            Important information about this page.
          </strong>
        </div>
        """;

    private readonly PlaywrightFixture _fixture = fixture;
    private readonly List<int> _trackedIds = [];
    private string _slug = "";

    public new async Task InitializeAsync()
    {
        await base.InitializeAsync();

        var (_, slug) = await SeedHelpers.SeedWikiPageReturningSlugAsync(
            _fixture.SeedClient,
            title: "warning-render",
            body: WarningTextBody,
            parentId: null,
            _trackedIds);

        _slug = slug;
    }

    public new async Task DisposeAsync()
    {
        foreach (var id in _trackedIds.AsEnumerable().Reverse())
        {
            await SeedHelpers.SoftDeleteWikiPageAsync(_fixture.SeedClient, id);
        }
        await base.DisposeAsync();
    }

    // --- IconHasTextBangAndCircleStyling ---

    [Fact]
    public async Task IconHasTextBangAndCircleStyling()
    {
        await Page.GotoAsync($"{_fixture.BaseUrl}/help/{_slug}");

        var icon = Page.Locator(".govuk-warning-text__icon");
        await Expect(icon).ToBeVisibleAsync();
        await Expect(icon).ToHaveTextAsync("!");

        var borderRadius = await icon.EvaluateAsync<string>("el => getComputedStyle(el).borderRadius");
        Assert.Equal("50%", borderRadius);

        var backgroundColor = await icon.EvaluateAsync<string>("el => getComputedStyle(el).backgroundColor");
        Assert.Equal("rgb(11, 12, 12)", backgroundColor);

        var color = await icon.EvaluateAsync<string>("el => getComputedStyle(el).color");
        Assert.Equal("rgb(255, 255, 255)", color);
    }
}
