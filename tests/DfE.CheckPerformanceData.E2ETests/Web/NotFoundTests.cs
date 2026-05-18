using DfE.CheckPerformanceData.E2ETests.Fixtures;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;

namespace DfE.CheckPerformanceData.E2ETests.Web;

[Collection("E2E")]
[Trait("Category", "W1")]
public sealed class NotFoundTests(PlaywrightFixture fixture) : PageTest
{
    private readonly PlaywrightFixture _fixture = fixture;

    // --- UnknownSlug_Returns404Cleanly ---

    [Fact]
    public async Task UnknownSlug_Returns404Cleanly()
    {
        var slug = $"e2e-{Guid.NewGuid():N}-nonexistent";

        var response = await Page.GotoAsync($"{_fixture.BaseUrl}/help/{slug}");

        Assert.NotNull(response);
        Assert.Equal(404, response!.Status);
    }
}
