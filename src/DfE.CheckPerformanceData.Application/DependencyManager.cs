using DfE.CheckPerformanceData.Application.Admin;
using DfE.CheckPerformanceData.Application.AmendmentRequests;
using DfE.CheckPerformanceData.Application.PageTree;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.ClaimsEnrichment;
using DfE.CheckPerformanceData.Application.Common;
using DfE.CheckPerformanceData.Application.ContentBlocks;
using DfE.CheckPerformanceData.Application.Countries;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.Journey.Conditions;
using DfE.CheckPerformanceData.Application.Journey.Validators;
using DfE.CheckPerformanceData.Application.LandingPage;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Application.Search;
using DfE.CheckPerformanceData.Application.WindowManagement;
using Microsoft.Extensions.DependencyInjection;

namespace DfE.CheckPerformanceData.Application;

public static class DependencyManager
{
    public static IServiceCollection AddApplicationDependencies(this IServiceCollection services)
    {
        services.AddScoped<IClaimsEnrichmentService, ClaimsEnrichmentService>();
        services.AddScoped<Dashboard.IOrganisationLoginRecorder, Dashboard.OrganisationLoginRecorder>();
        services.AddScoped<Dashboard.IDashboardService, Dashboard.DashboardService>();
        services.AddScoped<IContentBlockService, ContentBlockService>();
        services.AddScoped<IContentBlockSearchService, ContentBlockSearchService>();
        services.AddScoped<ISiteSearchService, SiteSearchService>();
        services.AddScoped<IAdminAccessPolicy, AdminAccessPolicy>();
        services.AddScoped<DefaultAdminAccessSeeder>();
        services.AddScoped<IPageNodeService, PageNodeService>();
        services.AddScoped<IPageNodeContentEditor, PageNodeContentEditor>();
        services.AddScoped<DefaultPageNodeSeeder>();
        services.AddScoped<SamplePageNodeSeeder>();
        services.AddScoped<Analytics.SampleSearchDataSeeder>();
        services.AddScoped<ContentStaging.IContentStagingService, ContentStaging.ContentStagingService>();
        services.AddScoped<IHtmlRenderingService, HtmlRenderingService>();
        services.AddScoped<Settings.ISettingService, Settings.SettingService>();
        services.AddScoped<ILandingPageService, LandingPageService>();
        services.AddScoped<IWindowService, WindowService>();
        // #315: the single place that compares an exercise's dates against the clock. Nothing else
        // in the solution may do that comparison for itself.
        services.AddScoped<ICheckingExerciseService, CheckingExerciseService>();
        services.AddScoped<ICheckYourPupilDataService, CheckYourPupilDataService>();
        // #317: which next-step options the check-your-pupil-data page may offer, from the open
        // exercises. The exercise-to-options map is domain knowledge, so it is not in the controller.
        services.AddScoped<INextStepsService, NextStepsService>();
        services.AddScoped<IJourneyValidationService, JourneyValidationService>();
        services.AddScoped<IRequestService, RequestService>();
        services.AddSingleton<IQuestionFlowService, QuestionFlowService>();
        services.AddScoped<ICountryService, CountryService>();
        services.AddScoped<IOptionVisibilityService, OptionVisibilityService>();
        services.AddScoped<IQuestionOptionalityService, QuestionOptionalityService>();
        services.AddScoped<IJourneyCondition, SchoolIsIndependentCondition>();
        services.AddScoped<IJourneyCondition, PupilIsAddBackCondition>();
        services.AddScoped<IJourneyCondition, PupilIsNotAddBackCondition>();
        services.AddScoped<IJourneyCondition, EalWouldBeAutoRejectedCondition>();
        services.AddScoped<IOriginCountryLanguageCapture, OriginCountryLanguageCapture>();
        services.AddScoped<IFormatValidator, DfeNumberFormatValidator>();
        // AB#296648: the cohort-count question. Registration is load-bearing — the journey engine
        // fails OPEN on an unregistered validator name, so a missing line here silently skips the
        // format check rather than throwing anywhere.
        services.AddScoped<IFormatValidator, WholeNumberFormatValidator>();
        // AB#298201: the missing-qualification enquiry's optional NCN field. Same load-bearing
        // registration reason as WholeNumberFormatValidator above.
        services.AddScoped<IFormatValidator, NcnValidator>();
        services.AddScoped<IAmendmentRequestsService, AmendmentRequestsService>();
        services.AddScoped<IUrnAmendmentRequestsService, UrnAmendmentRequestsService>();
        services.AddScoped<IWindowStatusService, WindowStatusService>();
        services.AddScoped<IBulkSubmissionService, BulkSubmissionService>();
        services.AddScoped<ISubmittedRequestService, SubmittedRequestService>();
        services.AddScoped<IEditAdviceService, EditAdviceService>();
        services.AddScoped<UncommittedRequests.IAdminRequestsService, UncommittedRequests.AdminRequestsService>();
        // AB#296648: the single derivation of "the second late results file has landed".
        services.AddScoped<ResultsEnquiry.ILateResultsAvailability, ResultsEnquiry.LateResultsAvailability>();

        services.AddSingleton<Observability.IHealthEvaluator, Observability.HealthEvaluator>();
        services.AddSingleton<Observability.StatusSentenceBuilder>();
        services.AddSingleton<ISearchResultCanonicaliser, SearchResultCanonicaliser>();

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
