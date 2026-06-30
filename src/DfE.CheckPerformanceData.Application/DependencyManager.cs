using DfE.CheckPerformanceData.Application.AmendmentRequests;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.ClaimsEnrichment;
using DfE.CheckPerformanceData.Application.Common;
using DfE.CheckPerformanceData.Application.ContentBlocks;
using DfE.CheckPerformanceData.Application.Countries;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.Journey.Conditions;
using DfE.CheckPerformanceData.Application.LandingPage;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Application.Wiki;
using Microsoft.Extensions.DependencyInjection;

namespace DfE.CheckPerformanceData.Application;

public static class DependencyManager
{
    public static IServiceCollection AddApplicationDependencies(this IServiceCollection services)
    {
        services.AddScoped<IClaimsEnrichmentService, ClaimsEnrichmentService>();
        services.AddScoped<IContentBlockService, ContentBlockService>();
        services.AddScoped<IContentBlockSearchService, ContentBlockSearchService>();
        services.AddScoped<ContentPages.IContentPageService, ContentPages.ContentPageService>();
        services.AddScoped<ContentPages.IContentPageEditor, ContentPages.ContentPageEditor>();
        services.AddScoped<ContentStaging.IContentStagingService, ContentStaging.ContentStagingService>();
        services.AddScoped<IHtmlRenderingService, HtmlRenderingService>();
        services.AddScoped<IWikiService, WikiService>();
        services.AddScoped<WikiSeeder>();
        services.AddScoped<Settings.ISettingService, Settings.SettingService>();
        services.AddScoped<ILandingPageService, LandingPageService>();
        services.AddScoped<ICheckYourPupilDataService, CheckYourPupilDataService>();
        services.AddScoped<IJourneyValidationService, JourneyValidationService>();
        services.AddScoped<IRequestService, RequestService>();
        services.AddSingleton<IQuestionFlowService, QuestionFlowService>();
        services.AddScoped<ICountryService, CountryService>();
        services.AddScoped<IOptionVisibilityService, OptionVisibilityService>();
        services.AddScoped<IJourneyCondition, SchoolIsIndependentCondition>();
        services.AddScoped<IAmendmentRequestsService, AmendmentRequestsService>();
        services.AddScoped<ISubmittedRequestService, SubmittedRequestService>();
        services.AddScoped<IEditAdviceService, EditAdviceService>();
        services.AddScoped<UncommittedRequests.IAdminRequestsService, UncommittedRequests.AdminRequestsService>();

        services.AddSingleton<Observability.IHealthEvaluator, Observability.HealthEvaluator>();
        services.AddSingleton<Observability.StatusSentenceBuilder>();

        services.AddRulesEngineDependencies();

        return services;
    }

    /// <summary>
    /// The rules-engine subset of the Application layer: pure singletons with no
    /// repository or external-client dependencies. The RulesEngineWorker host calls
    /// this instead of <see cref="AddApplicationDependencies"/> — the full set needs
    /// Persistence repositories, the DfE Sign-in client and the journey blob clients,
    /// which only the Web host registers.
    /// </summary>
    public static IServiceCollection AddRulesEngineDependencies(this IServiceCollection services)
    {
        services.AddSingleton<IRulesEngine, RulesEngine.RulesEngine>();
        services.AddSingleton<IRuleContextMapper, RuleContextMapper>();
        services.AddSingleton<RuleSetValidator>();
        services.AddSingleton<RulesConfig.LookupsValidator>();
        services.AddScoped<RulesConfig.IRulesConfigService, RulesConfig.RulesConfigService>();

        return services;
    }
}
