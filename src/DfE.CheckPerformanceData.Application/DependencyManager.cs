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
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DfE.CheckPerformanceData.Application;

public static class DependencyManager
{
    public static IServiceCollection AddApplicationDependencies(this IServiceCollection services)
    {
        // The clock every exercise and window gate below reads (#535): UK time, whatever zone the
        // container runs in. Replaced, not TryAdd-ed: ASP.NET's AddAuthentication() has already
        // registered the system clock by the time the web host calls this, and a TryAdd after it
        // does nothing.
        services.RemoveAll<TimeProvider>();
        services.AddSingleton<TimeProvider>(UkTimeProvider.Instance);

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
        services.AddScoped<ContentStaging.ManifestContentImporter>();
        services.AddScoped<Analytics.SampleSearchDataSeeder>();
        services.AddScoped<ContentStaging.IContentStagingService, ContentStaging.ContentStagingService>();
        services.AddScoped<ContentStaging.ContentBundleSanitiser>();
        services.AddScoped<IHtmlRenderingService, HtmlRenderingService>();
        services.AddScoped<Settings.ISettingService, Settings.SettingService>();
        services.AddScoped<SiteAssets.ISiteAssetService, SiteAssets.SiteAssetService>();
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
        // Registration is load-bearing: OptionVisibilityService and QuestionOptionalityService
        // resolve conditions from the container and treat an unregistered name as false, so a
        // condition named in a flow's visibleWhen without a line here silently hides that option
        // for EVERY school. Guarded by EveryImplementedCondition_IsRegisteredInTheContainer.
        services.AddScoped<IJourneyCondition, SchoolIsIndependentCondition>();
services.AddScoped<IJourneyCondition, SchoolIsNotIndependentCondition>();
        // AB#304119: needs INotOnRollCollegeListProvider, which the web host registers with the
        // blob clients (AddCpdBlobStorage), beside the hosted service that loads the list.
        services.AddScoped<IJourneyCondition, SchoolCanRecordNotOnRollCondition>();
        services.AddScoped<IJourneyCondition, PupilIsAddBackCondition>();
        services.AddScoped<IJourneyCondition, PupilIsNotAddBackCondition>();
        services.AddScoped<IJourneyCondition, EalWouldBeAutoRejectedCondition>();
        services.AddScoped<IJourneyCondition, NotOnRollReasonIsOtherCondition>();
        services.AddScoped<IOriginCountryLanguageCapture, OriginCountryLanguageCapture>();
        services.AddScoped<IFormatValidator, DfeNumberFormatValidator>();
        // AB#296648: the cohort-count question. Registration is load-bearing — the journey engine
        // fails OPEN on an unregistered validator name, so a missing line here silently skips the
        // format check rather than throwing anywhere.
        services.AddScoped<IFormatValidator, WholeNumberFormatValidator>();
        // AB#298201: the missing-qualification enquiry's optional NCN field. Same load-bearing
        // registration reason as WholeNumberFormatValidator above.
        services.AddScoped<IFormatValidator, NcnValidator>();
        // Issue 496: the permanent-exclusion DfE-number question's own message. Same load-bearing
        // registration reason as WholeNumberFormatValidator above; a missing line here would
        // silently skip the format check and let a malformed DfE number through.
        services.AddScoped<IFormatValidator, PermanentExclusionDfeNumberFormatValidator>();
        services.AddScoped<IAmendmentRequestsService, AmendmentRequestsService>();
        services.AddScoped<IUrnAmendmentRequestsService, UrnAmendmentRequestsService>();
        services.AddScoped<IWindowStatusService, WindowStatusService>();
        services.AddScoped<IBulkSubmissionService, BulkSubmissionService>();
        services.AddScoped<ISubmittedRequestService, SubmittedRequestService>();
        services.AddScoped<IEditAdviceService, EditAdviceService>();
        services.AddScoped<AdminRequests.IAdminRequestsService, AdminRequests.AdminRequestsService>();
        services.AddScoped<WindowManagement.ICloseExerciseService, WindowManagement.CloseExerciseService>();
        // AB#301022: ends an open exercise before its scheduled end. The admin Close action runs
        // this, then ICloseExerciseService above.
        services.AddScoped<WindowManagement.IExerciseEarlyClosureService, WindowManagement.ExerciseEarlyClosureService>();
        // AB#302158: runs ICloseExerciseService by itself two hours after an exercise ends. The
        // web host's ExerciseHandOverJob drives it; nothing in a request path calls it.
        services.AddScoped<WindowManagement.IAutomaticExerciseHandOver, WindowManagement.AutomaticExerciseHandOver>();
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
