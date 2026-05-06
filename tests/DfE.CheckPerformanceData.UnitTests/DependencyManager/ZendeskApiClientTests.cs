using DfE.CheckPerformanceData.Application.DfESignInApiClient;
using DfE.CheckPerformanceData.Application.ZendeskClient;
using DfE.CheckPerformanceData.Infrastructure;
using DfE.CheckPerformanceData.Infrastructure.DfeSignInApiClient;
using DfE.CheckPerformanceData.Infrastructure.ZendeskClient;
using DfE.CheckPerformanceData.Infrastructure.ZendeskClient.Services;
using DfE.CheckPerformanceData.Infrastructure.ZendeskClient;
using FluentAssertions;
using NSubstitute;
using NSubstitute.Extensions;


using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
namespace DfE.CheckPerformanceData.UnitTests;

public class ZendeskApiClientTests
{
    #region AddZendeskApiClient

    private IConfiguration CreateMockConfiguration()
    {
        var myConfiguration = new Dictionary<string, string>
        {
            { $"{ZendeskSettings.SectionName}:Subdomain", "test-subdomain" },
            { $"{ZendeskSettings.SectionName}:Domain", "example" },
            { $"{ZendeskSettings.SectionName}:Email", "test@example.com" },
            { $"{ZendeskSettings.SectionName}:ApiToken", "example" },
            { $"{PollySettings.SectionName}:MaxRetryAttempts", "5" },
            { $"{PollySettings.SectionName}:BaseDelayMilliseconds", "100" },
            { $"{PollySettings.SectionName}:JitterMilliseconds", "200" }
        };

        return new ConfigurationBuilder()
            .AddInMemoryCollection(myConfiguration)
            .Build();
    }


    [Fact]
    public void AddZendeskApiClient_WithValidConfiguration_AddsRefitClientService()
    {
        // Arrange


        var services = new ServiceCollection();

        var config = CreateMockConfiguration();
        
        
        // Act

        // var settings = config.GetSection(ZendeskSettings.SectionName).Get<ZendeskSettings>();
        var result = DependencyManager.AddZendeskApiClient(services, config);

        // Assert
        result.Should().BeSameAs(services);
        services.Should().NotBeNull();
    }

    [Fact]
    public void AddZendeskApiClient_ThrowsInvalidOperationException_WhenSettingsMissing()
    {
        // Arrange
        var services = new ServiceCollection();
        var mockConfig = Substitute.For<IConfiguration>();
        var mockSection = Substitute.For<IConfigurationSection>();

        // Simulate key-value pairs for the binder
        var children = new List<IConfigurationSection>
        {
           
        };

        mockSection.GetChildren().Returns(children);
        mockConfig.GetSection(ZendeskSettings.SectionName).Returns(mockSection);
        // Act & Assert
        Action act = () => DependencyManager.AddZendeskApiClient(services, mockConfig);
        
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ZendeskSettings section is missing*");
    }

    [Fact]
    public void AddZendeskApiClient_ConfiguresRefitClientForIZendeskApi()
    {
        // Arrange
        var services = new ServiceCollection();
        var config = CreateMockConfiguration();

        // Act
        DependencyManager.AddZendeskApiClient(services, config);

        // Assert
        var serviceProvider = services.BuildServiceProvider();

        // 1. Resolve the factory that Refit uses under the hood
        var client = serviceProvider.GetRequiredService<IZendeskApi>();


        // 3. Verify the BaseAddress
        client.Should().NotBeNull();
        
    }


    [Fact]
    public void AddZendeskApiClient_AddsZendeskServiceAsScoped()
    {
        // Arrange
        var services = new ServiceCollection();
        var config = CreateMockConfiguration();

        // Act
        DependencyManager.AddZendeskApiClient(services, config);

        // Assert
        var descriptor = services.SingleOrDefault(sd => sd.ServiceType == typeof(IZendeskService));
        descriptor.Should().NotBeNull();
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    [Fact]
    public void AddZendeskApiClient_AddsZendeskAttachmentServiceAsScoped()
    {
        // Arrange
        var services = new ServiceCollection();
        var config = CreateMockConfiguration();

        // Act
        DependencyManager.AddZendeskApiClient(services, config);

        // Assert
        var descriptor = services.SingleOrDefault(sd => sd.ServiceType == typeof(IZendeskAttachmentService));
        descriptor.Should().NotBeNull();
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    [Fact]
    public void AddZendeskApiClient_AddsRefitLoggingHandlerAsTransient()
    {
        // Arrange
        var services = new ServiceCollection();
        var config = CreateMockConfiguration();

        // Act
        DependencyManager.AddZendeskApiClient(services, config);

        // Assert
        var descriptor = services.SingleOrDefault(sd => sd.ServiceType == typeof(RefitLoggingHandler));
        descriptor.Should().NotBeNull();
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Transient);
    }

    [Fact]
    public void AddZendeskApiClient_AddsPollySettingsAsScoped()
    {
        // Arrange
        var services = new ServiceCollection();
        var config = CreateMockConfiguration();

        // Act
        DependencyManager.AddZendeskApiClient(services, config);

        // Assert
        var descriptor = services.BuildServiceProvider().GetService<IOptions<PollySettings>>();
        descriptor.Should().NotBeNull();
        descriptor!.Value.MaxRetryAttempts.Should().Be(5);
        descriptor.Value.BaseDelayMilliseconds.Should().Be(100);    
        descriptor.Value.JitterMilliseconds.Should().Be(200);

    }

    #endregion
}