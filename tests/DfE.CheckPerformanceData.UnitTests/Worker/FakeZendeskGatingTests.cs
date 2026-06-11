using DfE.CheckPerformanceData.Application.ZendeskClient;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.RulesEngineWorker.Zendesk;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DfE.CheckPerformanceData.Application.UnitTests.Worker;

// The fake/outbox Zendesk service is selected by the Zendesk:UseFake config flag, not by the
// environment name. The DfE test site runs ASPNETCORE_ENVIRONMENT=Development, so an
// environment-name gate cannot keep the fake on test while turning it off elsewhere. The flag
// defaults to true so a fresh dev/test environment never pushes to real Zendesk, and flipping
// it to false routes to the real client with no code change.
public sealed class FakeZendeskGatingTests
{
    private sealed class StubZendeskService : IZendeskService
    {
        public Task<ListViewsResponseDto> ListViewsAsync(int? pageSize) => throw new NotSupportedException();
        public Task<ListViewTicketsResponseDto> GetTicketsForView(long viewId, Dictionary<string, object>? query = null) => throw new NotSupportedException();
        public Task<TicketFieldsResponseDto> GetTicketFields() => throw new NotSupportedException();
        public Task<UserFieldsResponseDto> GetUserFieldsAsync() => throw new NotSupportedException();
        public Task<CreateTicketResponseDto> CreateTicketAsync(CreateTicketRequestDto request) => throw new NotSupportedException();
        public Task<TicketsViewModel> GetTicketsViewModelAsync(long viewId, Dictionary<string, object>? query = null) => throw new NotSupportedException();
        public Task<GetTicketResponseDto> GetTicketAsync(long ticketId) => throw new NotSupportedException();
        public Task<TicketCommentsResponseDto> GetTicketComments(long ticketId) => throw new NotSupportedException();
        public Task<GetTicketViewModel> GetTicketViewModelAsync(long ticketId) => throw new NotSupportedException();
    }

    private static IServiceCollection BuildServicesWithRealZendesk()
    {
        var services = new ServiceCollection();
        // Stand in for the real Zendesk client registered by AddZendeskApiClient, so the
        // composition-root helper has a registration to replace.
        services.AddScoped<IZendeskService, StubZendeskService>();
        return services;
    }

    private static IConfiguration Config(bool? useFake)
    {
        var values = new Dictionary<string, string?>();
        if (useFake.HasValue)
            values["Zendesk:UseFake"] = useFake.Value ? "true" : "false";

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    [Fact]
    public void UseFakeTrue_SelectsDevOutboxFake()
    {
        var services = BuildServicesWithRealZendesk();

        ZendeskServiceRegistration.ConfigureFakeZendesk(services, Config(useFake: true));

        Assert.Equal(typeof(DevOutboxZendeskService), ResolveZendeskImplementationType(services));
    }

    [Fact]
    public void UseFakeFalse_KeepsRealZendesk()
    {
        var services = BuildServicesWithRealZendesk();

        ZendeskServiceRegistration.ConfigureFakeZendesk(services, Config(useFake: false));

        Assert.Equal(typeof(StubZendeskService), ResolveZendeskImplementationType(services));
    }

    [Fact]
    public void UseFakeAbsent_DefaultsToFake()
    {
        var services = BuildServicesWithRealZendesk();

        ZendeskServiceRegistration.ConfigureFakeZendesk(services, Config(useFake: null));

        Assert.Equal(typeof(DevOutboxZendeskService), ResolveZendeskImplementationType(services));
    }

    private static Type? ResolveZendeskImplementationType(IServiceCollection services)
    {
        var descriptor = services.Last(d => d.ServiceType == typeof(IZendeskService));
        return descriptor.ImplementationType;
    }
}
