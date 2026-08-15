using DfE.CheckPerformanceData.Application.ContentStaging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;

namespace DfE.CheckPerformanceData.Web.Startup;

public static class CoreWebExtensions
{
    public static WebApplicationBuilder AddCpdCoreWeb(this WebApplicationBuilder builder)
    {
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
            options.KnownProxies.Clear();
        });

        builder.Services.AddHttpContextAccessor();

        builder.Services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-XSRF-TOKEN";
        });

        // Setting to null to allow controller-level request size limits
        builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = null);

        builder.Services.AddControllersWithViews(options =>
        {
            // The binder's own ceiling on how many elements it will materialise into a bound
            // collection, defaulted to 1024. The content-staging confirm step offers a decision
            // per item and a bundle may legitimately carry more than that, so the limit has to
            // rise or the post fails — and it fails as an unhandled exception, not a model error.
            //
            // Raising it here is app-wide but not an app-wide exposure: FormOptions.ValueCountLimit
            // still caps every other endpoint at 1024 form values, so nothing else can present a
            // collection this large in the first place. Only the endpoints that opt in with
            // [RequestFormLimits] can reach the higher number.
            options.MaxModelBindingCollectionSize = ContentStagingFormLimits.MaxDecisions;
        });

        builder.Services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
        });

        builder.Services.AddMemoryCache();

        builder.Services.AddHealthChecks();

        return builder;
    }
}
