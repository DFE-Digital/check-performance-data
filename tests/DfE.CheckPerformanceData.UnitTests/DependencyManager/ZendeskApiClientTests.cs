using DfE.CheckPerformanceData.Application.DfESignInApiClient;
using DfE.CheckPerformanceData.Application.ZendeskClient;
using DfE.CheckPerformanceData.Infrastructure;
using DfE.CheckPerformanceData.Infrastructure.DfeSignInApiClient;
using DfE.CheckPerformanceData.Infrastructure.ZendeskClient;
using DfE.CheckPerformanceData.Infrastructure.ZendeskClient.Services;
using NSubstitute;
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
            { $"{ZendeskSettings.SectionName}:ClientId", "test-client-id" },
            { $"{ZendeskSettings.SectionName}:ClientSecret", "example-secret" },
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
        Assert.Same(services, result);
        Assert.NotNull(services);
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
        
        var exception = Assert.Throws<InvalidOperationException>(act);
        Assert.Contains("ZendeskSettings section is missing", exception.Message);
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
        Assert.NotNull(client);
        
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
        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Scoped, descriptor!.Lifetime);
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
        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Scoped, descriptor!.Lifetime);
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
        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Transient, descriptor!.Lifetime);
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
        Assert.NotNull(descriptor);
        Assert.Equal(5, descriptor!.Value.MaxRetryAttempts);
        Assert.Equal(100, descriptor.Value.BaseDelayMilliseconds);    
        Assert.Equal(200, descriptor.Value.JitterMilliseconds);

    }

    // docker-compose passes SchoolCheckingExercise__GroupId / __BrandId through from .env
    // unconditionally, and .env.example ships both blank — so on a fresh clone the keys are
    // PRESENT with an empty string rather than absent. Binding "" to a non-nullable long threw
    // InvalidOperationException inside OptionsFactory, which killed rules_engine at host start
    // (exit 139) before it consumed a single message. The IDs are genuinely optional — every
    // consumer already falls back to 0 — so blank must bind to "not configured", not crash.
    private static IConfiguration CreateCheckingExerciseConfiguration(string groupId, string brandId)
    {
        var settings = new Dictionary<string, string?>
        {
            { $"{ZendeskSettings.SectionName}:Subdomain", "test-subdomain" },
            { $"{ZendeskSettings.SectionName}:Domain", "example" },
            { $"{ZendeskSettings.SectionName}:ClientId", "test-client-id" },
            { $"{ZendeskSettings.SectionName}:ClientSecret", "example-secret" },
            { $"{SchoolCheckingExerciseSettings.SectionName}:TargetViewTitle", "Schools checking exercise View" },
            { $"{SchoolCheckingExerciseSettings.SectionName}:GroupId", groupId },
            { $"{SchoolCheckingExerciseSettings.SectionName}:BrandId", brandId },
        };

        return new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
    }

    [Fact]
    public void AddZendeskApiClient_WithBlankCheckingExerciseIds_BindsToNullInsteadOfThrowing()
    {
        var services = new ServiceCollection();
        DependencyManager.AddZendeskApiClient(services, CreateCheckingExerciseConfiguration(string.Empty, string.Empty));

        var options = services.BuildServiceProvider().GetRequiredService<IOptions<SchoolCheckingExerciseSettings>>();

        Assert.Null(options.Value.GroupId);
        Assert.Null(options.Value.BrandId);
    }

    [Fact]
    public void AddZendeskApiClient_WithPopulatedCheckingExerciseIds_BindsTheConfiguredValues()
    {
        var services = new ServiceCollection();
        DependencyManager.AddZendeskApiClient(services, CreateCheckingExerciseConfiguration("360000123456", "42"));

        var options = services.BuildServiceProvider().GetRequiredService<IOptions<SchoolCheckingExerciseSettings>>();

        Assert.Equal(360000123456L, options.Value.GroupId);
        Assert.Equal(42L, options.Value.BrandId);
    }

    #endregion
}
