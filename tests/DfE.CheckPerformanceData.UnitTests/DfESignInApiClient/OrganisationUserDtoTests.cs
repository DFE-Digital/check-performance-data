using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DfE.CheckPerformanceData.Application.DfESignInApiClient;
using DfE.CheckPerformanceData.Infrastructure.DfeSignInApiClient;
using Microsoft.Extensions.Options;

namespace DfE.CheckPerformanceData.UnitTests.DfESignInApiClient;

public class OrganisationUserDtoTests
{
    [Fact]
    public void Deserialize_PopulatesUsersAndRoles_FromApiJson()
    {
        const string json = """
        {
          "users": [
            {
              "firstName": "John",
              "lastName": "Smith",
              "email": "john.smith@example.com",
              "userStatus": 1,
              "roles": [
                "APR",
                "SUB"
              ]
            }
          ]
        }
        """;

        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var response = JsonSerializer.Deserialize<OrganisationUsersResponseDto>(json, options);

        Assert.NotNull(response);
        Assert.Single(response!.Users);
        Assert.Equal("John", response.Users[0].FirstName);
        Assert.Equal("Smith", response.Users[0].LastName);
        Assert.Equal("john.smith@example.com", response.Users[0].Email);
        Assert.Equal(2, response.Users[0].Roles.Count);
        Assert.Equal("APR", response.Users[0].Roles[0].Code);
    }

    [Fact]
    public void Deserialize_EmptyResponse_ReturnsEmptyUsersList()
    {
        const string json = """
        {
          "users": []
        }
        """;

        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var response = JsonSerializer.Deserialize<OrganisationUsersResponseDto>(json, options);

        Assert.NotNull(response);
        Assert.Empty(response!.Users);
    }

    [Fact]
    public async Task GetOrganisationUsersAsync_ConstructsCorrectUrlWithRoles()
    {
        var responseJson = """
        {
          "users": []
        }
        """;

        string? capturedUrl = null;
        var handler = new MockHttpMessageHandler(async request =>
        {
            capturedUrl = request.RequestUri?.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson)
            };
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.example.com/") };
        var apiClient = new DfeSignInApiClient(client, Options.Create(new DfeSigninSettings { ServiceId = "svc-1", ApiClientSecret = "secret", ClientId = "client", Audience = "audience" }));

        var result = await apiClient.GetOrganisationUsersAsync("12345", ["APR", "SUB"]);

        Assert.NotNull(result);
        Assert.Contains("organisations/12345/users?roles=APR%2CSUB", capturedUrl!);
    }

    [Fact]
    public async Task GetOrganisationUsersAsync_EmptyContent_ReturnsNull()
    {
        var handler = new MockHttpMessageHandler(async _ =>
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("")
            };
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.example.com/") };
        var apiClient = new DfeSignInApiClient(client, Options.Create(new DfeSigninSettings { ServiceId = "svc-1", ApiClientSecret = "secret", ClientId = "client", Audience = "audience" }));

        var result = await apiClient.GetOrganisationUsersAsync("12345", ["APR"]);

        Assert.Null(result);
    }

    private class MockHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responseFunc) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => responseFunc(request);
    }
}
