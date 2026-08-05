using Microsoft.AspNetCore.HttpOverrides;

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

        builder.Services.AddControllersWithViews();

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
