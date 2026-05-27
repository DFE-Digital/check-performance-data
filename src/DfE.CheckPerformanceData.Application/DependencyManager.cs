using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.ClaimsEnrichment;
using DfE.CheckPerformanceData.Application.Common;
using DfE.CheckPerformanceData.Application.ContentBlocks;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.LandingPage;
using DfE.CheckPerformanceData.Application.RequestSubmission;
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
        services.AddScoped<WikiSeeder>();
        services.AddScoped<Settings.ISettingService, Settings.SettingService>();
        services.AddScoped<ILandingPageService, LandingPageService>();
        services.AddScoped<ICheckYourPupilDataService, CheckYourPupilDataService>();
        services.AddScoped<IJourneyValidationService, JourneyValidationService>();
        services.AddScoped<IRequestService, RequestService>();
        services.AddSingleton<IQuestionFlowService, QuestionFlowService>();

        return services;
    }
}
