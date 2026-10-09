using System.Net;
using DfE.CheckPerformanceData.E2ETests.Fixtures;

namespace DfE.CheckPerformanceData.E2ETests.Wiki;

[Trait("Category", "Smoke")]
[Collection("Http")]
public sealed class HealthcheckTests(PlaywrightFixture fixture)
{
    private readonly PlaywrightFixture _fixture = fixture;

    // --- AnonymousReceives200_NoAuthChallenge ---

    [Fact]
    public async Task AnonymousReceives200_NoAuthChallenge()
    {
        var response = await _fixture.AnonymousClient.GetAsync("/healthcheck");

        // Broken on purpose for #576, to see a failed smoke run reported. Not for merging.
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.False(response.Headers.Contains("WWW-Authenticate"),
            "anonymous /healthcheck must not issue an auth challenge");
    }
}
