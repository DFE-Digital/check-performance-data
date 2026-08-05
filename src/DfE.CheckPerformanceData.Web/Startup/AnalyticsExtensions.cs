using Dfe.Analytics;
using Dfe.Analytics.AspNetCore;
using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Infrastructure.Analytics;
using DfE.CheckPerformanceData.Web.Analytics;

namespace DfE.CheckPerformanceData.Web.Startup;

public static class AnalyticsExtensions
{
    // Single source of truth for "is BigQuery analytics configured": deployed envs wire
    // DfeAnalytics:* via Terraform; dev/review/local boot without GCP. Registration and
    // middleware both call this so no flag needs threading through Program.cs.
    private static bool IsEnabled(IConfiguration configuration) =>
        !string.IsNullOrEmpty(configuration["DfeAnalytics:DatasetId"]);

    public static IServiceCollection AddCpdBigQueryAnalytics(this IServiceCollection services, IConfiguration configuration)
    {
        // DfE Analytics: stream a web_request event to BigQuery per request when configured.
        // Deployed envs wire DfeAnalytics:* via Terraform; guarded on DatasetId so dev,
        // review and local boot without GCP. The matching middleware is added below under
        // the same flag. The RequestFilter (see AnalyticsRequestFilter) keeps health probes,
        // static assets and scanner/bot noise out of the dataset.
        if (IsEnabled(configuration))
        {
            services
                .AddDfeAnalytics()
                .AddAspNetCoreIntegration(options =>
                    options.RequestFilter = AnalyticsRequestFilter.ShouldTrack);

            services.AddSingleton<IWebRequestEventEnricher, OrganisationEventEnricher>();
            // Custom events go through the same IEventSender (AspNetCoreEventSender), so each
            // is sent as its own row, auto-enriched with request + organisation context.
            services.AddTransient<IAnalyticsService, DfeAnalyticsService>();
        }
        else
        {
            // No-op so controllers can always inject IAnalyticsService; dev/review/local
            // boot without GCP.
            services.AddSingleton<IAnalyticsService, NullAnalyticsService>();
        }

        return services;
    }

    public static WebApplication UseCpdBigQueryAnalytics(this WebApplication app)
    {
        // After auth so the event captures the signed-in user's id + organisation claims;
        // the RequestFilter configured above excludes health probes from the dataset.
        if (IsEnabled(app.Configuration))
            app.UseDfeAnalytics();

        return app;
    }
}
