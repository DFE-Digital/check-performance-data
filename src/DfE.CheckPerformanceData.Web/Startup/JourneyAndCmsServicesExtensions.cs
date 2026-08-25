using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.FileStorage;
using DfE.CheckPerformanceData.Infrastructure.BlobStorage;
using DfE.CheckPerformanceData.Web.Controllers.Journey;
using DfE.CheckPerformanceData.Web.PageTree;
using DfE.CheckPerformanceData.Web.Seeding;
using DfE.CheckPerformanceData.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace DfE.CheckPerformanceData.Web.Startup;

public static class JourneyAndCmsServicesExtensions
{
    public static IServiceCollection AddCpdJourneyAndCmsServices(this IServiceCollection services)
    {
        // Orchestrates the full dev-data seeding sequence, shared by startup seeding (below) and
        // the admin Danger zone "Reset seed data" action.
        services.AddScoped<IDevDataSeedingOrchestrator, DevDataSeedingOrchestrator>();

        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddScoped<IFileStorageService, EvidenceBlobStorageService>();
        services.AddScoped<IJourneyViewModelBuilder, JourneyViewModelBuilder>();

        services.AddScoped<DfE.CheckPerformanceData.Web.Controllers.DevPipelineRunner>();
        services.AddScoped<DfE.CheckPerformanceData.Web.Services.GuidanceContentCopyService>();

        services.AddSingleton<IReservedRouteProvider, EndpointReservedRouteProvider>();
        services.AddScoped<PageNodePathValidator>();

        return services;
    }
}
