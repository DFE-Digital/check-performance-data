using System.Net;
using DfE.CheckPerformanceData.Infrastructure.DfeSignInApiClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DfE.CheckPerformanceData.Application.UnitTests.DfESignInApiClient;

public sealed class DfeSignInApiClientTests
{
    private static DfeSignInApiClient CreateClient(HttpResponseMessage response)
    {
        var httpClient = new HttpClient(new StubHandler(response))
        {
            BaseAddress = new Uri("https://example.com/")
        };
        var settings = Options.Create(new DfeSigninSettings
        {
            ApiClientSecret = "secret",
            ClientId = "client",
            Audience = "audience",
            ServiceId = "service-id"
        });
        return new DfeSignInApiClient(httpClient, settings, NullLogger<DfeSignInApiClient>.Instance);
    }

    [Fact]
    public async Task GetUserRolesAsync_Returns_Empty_When_Api_Responds_404()
    {
        // DfE Sign-in returns 404 (not an empty list) when the user has no
        // access record for this service + organisation. That is a legitimate
        // "no roles" state, not a transport error, so it must not throw.
        var client = CreateClient(new HttpResponseMessage(HttpStatusCode.NotFound));

        var roles = await client.GetUserRolesAsync("org-id", "user-id");

        Assert.Empty(roles);
    }

    [Fact]
    public async Task GetOrganisationAsync_Returns_Null_When_Api_Responds_404()
    {
        var client = CreateClient(new HttpResponseMessage(HttpStatusCode.NotFound));

        var organisation = await client.GetOrganisationAsync("user-id", "org-id");

        Assert.Null(organisation);
    }

    private sealed class StubHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(response);
    }
}
