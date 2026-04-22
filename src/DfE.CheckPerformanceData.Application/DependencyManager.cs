using DfE.CheckPerformanceData.Application.ClaimsEnrichment;
using DfE.CheckPerformanceData.Application.Common;
using DfE.CheckPerformanceData.Application.ContentBlocks;
using DfE.CheckPerformanceData.Application.Features.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Features.LandingPage;
using DfE.CheckPerformanceData.Application.Wiki;
using Microsoft.Extensions.DependencyInjection;

namespace DfE.CheckPerformanceData.Application;

public static class DependencyManager
{
    public static IServiceCollection AddApplicationDependencies(this IServiceCollection services)
    {
        services.AddScoped<IClaimsEnrichmentService, ClaimsEnrichmentService>();
        services.AddScoped<IContentBlockService, ContentBlockService>();
        services.AddScoped<IHtmlRenderingService, HtmlRenderingService>();
        services.AddScoped<IWikiService, WikiService>();
        services.AddScoped<ILandingPageService, LandingPageService>();
        services.AddScoped<ICheckYourPupilDataService, CheckYourPupilDataService>();

        return services;
    }
}
