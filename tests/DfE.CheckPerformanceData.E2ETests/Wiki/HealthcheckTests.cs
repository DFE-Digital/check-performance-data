using System.Net;
using DfE.CheckPerformanceData.E2ETests.Fixtures;

namespace DfE.CheckPerformanceData.E2ETests.Wiki;

[Collection("E2E")]
[Trait("Category", "W1")]
public sealed class HealthcheckTests(PlaywrightFixture fixture)
{
    private readonly PlaywrightFixture _fixture = fixture;

    // --- AnonymousReceives200_NoAuthChallenge ---

    [Fact]
    public async Task AnonymousReceives200_NoAuthChallenge()
    {
        var response = await _fixture.SeedClient.GetAsync("/healthcheck");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("WWW-Authenticate"),
            "anonymous /healthcheck must not issue an auth challenge");
    }
}
