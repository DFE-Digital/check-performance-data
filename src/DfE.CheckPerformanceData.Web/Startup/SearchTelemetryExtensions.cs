using DfE.CheckPerformanceData.Application.Search;
using DfE.CheckPerformanceData.Web.Analytics;

namespace DfE.CheckPerformanceData.Web.Startup;

public static class SearchTelemetryExtensions
{
    public static IServiceCollection AddCpdSearchTelemetry(this IServiceCollection services)
    {
        // Gates the /search <!-- rank: N --> debug comment AND the log-level promotion of
        // per-hit / per-exclusion telemetry breadcrumbs. Backed by the CMS settings store so
        // operators can flip CMS:SearchDebugOn from the admin settings page without a
        // container restart. Scoped because it captures ISettingService (Scoped) — registering
        // as Singleton would pin the first request's settings-repository to the process lifetime
        // and starve subsequent requests of the current toggle value.
        services.AddScoped<
            DfE.CheckPerformanceData.Application.Search.ISearchDebugOptions,
            DfE.CheckPerformanceData.Application.Search.CmsSettingsSearchDebugOptions>();

        // Emits a structured log per search request (Info summary, Debug/Info per-hit and
        // per-exclusion depending on the debug toggle, Warn zero-result). Registered as the
        // CONCRETE type so the composite decorator below can resolve it directly without a
        // self-recursive lookup through ISearchTelemetry. Scoped because it depends on
        // Scoped ISearchDebugOptions — a Scoped service captured inside a Singleton would
        // leak the first request's toggle read across every subsequent request.
        services.AddScoped<DfE.CheckPerformanceData.Application.Search.LoggerSearchTelemetry>();

        // Composite decorator: wraps LoggerSearchTelemetry (as the inner) with an enqueue
        // onto the analytics channel. Registered against ISearchTelemetry so every /search
        // caller resolves the fan-out shape; the inner logger keeps working exactly as
        // before.
        services.AddScoped<
            DfE.CheckPerformanceData.Application.Search.ISearchTelemetry,
            DfE.CheckPerformanceData.Application.Analytics.SinkAndLogSearchTelemetry>();

        // Analytics channel + writer + drop counter. The channel is a singleton (the same
        // instance must be seen by every request-scope decorator and the singleton writer
        // that drains it). The ChannelWriter side is factory-registered so the decorator's
        // ctor gets a typed writer without having to know about the wrapper.
        services.AddSingleton<DfE.CheckPerformanceData.Application.Analytics.SearchAnalyticsChannel>();
        services.AddSingleton(sp =>
            sp.GetRequiredService<DfE.CheckPerformanceData.Application.Analytics.SearchAnalyticsChannel>()
              .Channel.Writer);
        services.AddSingleton<
            DfE.CheckPerformanceData.Application.Analytics.ISearchAnalyticsDroppedCounter,
            DfE.CheckPerformanceData.Application.Analytics.SearchAnalyticsDroppedCounter>();
        services.AddScoped<
            DfE.CheckPerformanceData.Application.Analytics.ISearchAnalyticsSessionProvider,
            DfE.CheckPerformanceData.Web.Analytics.HttpContextSearchAnalyticsSessionProvider>();
        services.AddHostedService<DfE.CheckPerformanceData.Web.Analytics.SearchEventWriter>();

        // In-memory job store for the dev-only seed-sample-search-data admin page. Singleton
        // because the POST creates a job on one request and subsequent GETs (progress polls +
        // Cancel DELETE) must read the same instance across the request lifetime. Backed by
        // a ConcurrentDictionary; auto-evicts completed jobs after 5 minutes on new-job insert.
        services.AddSingleton<
            DfE.CheckPerformanceData.Application.Analytics.ISampleSearchDataSeedJobStore,
            DfE.CheckPerformanceData.Application.Analytics.InMemorySampleSearchDataSeedJobStore>();

        // Process-lifetime counter of zero-result searches. Interlocked-backed; safe under
        // concurrent request throughput. Read out-of-band by diagnostic surfaces.
        services.AddSingleton<
            DfE.CheckPerformanceData.Application.Search.ISearchZeroResultsCounter,
            DfE.CheckPerformanceData.Application.Search.SearchZeroResultsCounter>();

        return services;
    }
}
