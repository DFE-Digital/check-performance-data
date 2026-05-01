using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;

namespace DfE.CheckPerformanceData.E2ETests.Wiki;

[Collection("E2E")]
[Trait("Category", "W1")]
public sealed class ContentBlockRenderTests(PlaywrightFixture fixture) : PageTest, IAsyncLifetime
{
    private readonly PlaywrightFixture _fixture = fixture;
    private string _key = "";
    private string _markerText = "";

    public new async Task InitializeAsync()
    {
        await base.InitializeAsync();

        _markerText = $"hello-e2e-{Guid.NewGuid():N}";
        _key = await SeedHelpers.SeedContentBlockAsync(
            _fixture.SeedClient,
            keyPrefix: "block-test",
            value: $"<p>{_markerText}</p>");
    }

    public new Task DisposeAsync()
    {
        // Content-block leak accepted: no DELETE route on ContentBlockController. UUID-prefixed
        // keys make leaked test data harmless across runs.
        return base.DisposeAsync();
    }

    // --- RendersSanitisedContent ---

    [Fact]
    public async Task RendersSanitisedContent()
    {
        await Page.GotoAsync($"{_fixture.BaseUrl}/content-block/versions/{_key}");

        // The version-history view hides each preview panel by default; clicking a row
        // toggles its hidden attribute off. Click the only version row to reveal the
        // seeded panel before asserting visibility.
        await Page.Locator("tr.version-row").First.ClickAsync();

        await Expect(Page.GetByText(_markerText)).ToBeVisibleAsync();
    }
}
