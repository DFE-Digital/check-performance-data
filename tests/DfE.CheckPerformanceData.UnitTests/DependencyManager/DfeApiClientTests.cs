using DfE.CheckPerformanceData.Application.DfESignInApiClient;
using DfE.CheckPerformanceData.Infrastructure;
using DfE.CheckPerformanceData.Infrastructure.DfeSignInApiClient;
using DfE.CheckPerformanceData.Infrastructure.ZendeskClient;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Refit;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;

namespace DfE.CheckPerformanceData.UnitTests;

public class DfeApiClientTests
{
    #region AddDfeApiClient

    private IConfiguration CreateMockConfiguration()
    {
        var myConfiguration = new Dictionary<string, string>
        {
            { $"{DfeSigninSettings.SectionName}:MetadataAddress", "https://example.com/metadata" },
            { $"{DfeSigninSettings.SectionName}:ClientId", "test-client-id" },
            { $"{DfeSigninSettings.SectionName}:ClientSecret", "test-client-secret" },
            { $"{DfeSigninSettings.SectionName}:Audience", "test-audience" },
            { $"{DfeSigninSettings.SectionName}:BaseUrl", "https://example.com" },
            { $"{DfeSigninSettings.SectionName}:ApiClientSecret", "test-api-client-secret" }
        };

        return new ConfigurationBuilder()
            .AddInMemoryCollection(myConfiguration)
            .Build();
    }

    [Fact]
    public void AddDfeApiClient_WithValidConfiguration_AddsHttpClientService()
    {
        // Arrange
        var services = new ServiceCollection();
       
        var config = CreateMockConfiguration();
        // Act
        var result = DependencyManager.AddDfeApiClient(services, config);

        // Assert
        result.Should().BeSameAs(services);
        services.Should().NotBeNull();
    }

    [Fact]
    public void AddDfeApiClient_ConfiguresHttpClientForDfeSignInApiClient()
    {
        // Arrange
        var services = new ServiceCollection();
        var config = CreateMockConfiguration();

        // Act
        DependencyManager.AddDfeApiClient(services, config);

        // Assert
        var descriptor = services.SingleOrDefault(sd => sd.ServiceType == typeof(IDfESignInApiClient));
        descriptor.Should().NotBeNull();
    }








    #endregion
}