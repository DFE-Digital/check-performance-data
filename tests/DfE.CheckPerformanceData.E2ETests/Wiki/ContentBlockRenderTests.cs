using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;

namespace DfE.CheckPerformanceData.E2ETests.Wiki;

[Collection("E2E")]
public sealed class ContentBlockRenderTests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    // Content-block leak accepted: no DELETE route on ContentBlockController. UUID-prefixed
    // keys make leaked test data harmless across runs. TrackedIds stays empty so the base
    // class teardown is a no-op for this fixture.
    private string _key = "";
    private string _markerText = "";

    protected override async Task SeedAsync()
    {
        _markerText = $"hello-e2e-{Guid.NewGuid():N}";
        _key = await SeedHelpers.SeedContentBlockAsync(
            Fixture.SeedClient,
            keyPrefix: "block-test",
            value: $"<p>{_markerText}</p>");
    }

    // --- RendersSanitisedContent ---

    [Fact]
    public async Task RendersSanitisedContent()
    {
        await Page.GotoAsync($"{Fixture.BaseUrl}/content-block/versions/{_key}");

        // The version-history view hides each preview panel by default; clicking a row
        // toggles its hidden attribute off. Click the only version row to reveal the
        // seeded panel before asserting visibility.
        await Page.Locator("tr.version-row").First.ClickAsync();

        await Expect(Page.GetByText(_markerText)).ToBeVisibleAsync();
    }
}
