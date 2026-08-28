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
            // Cookie.SecurePolicy is deliberately left at its default. Setting it to Always
            // outside development looks like the obvious counterpart to the session cookie, but
            // the antiforgery system does not merely mark the cookie: DefaultAntiforgery
            // .CheckSSLConfig throws when the policy is Always and the request is not HTTPS, and
            // _Layout mints a token on every page render. Deployed pods sit behind a
            // TLS-terminating ingress and receive plain HTTP, and UseForwardedHeaders does not
            // correct Request.IsHttps here because KnownProxies.Clear() above leaves
            // KnownNetworks at its loopback default, so the ingress's X-Forwarded-Proto is
            // dropped. The result is a 500 on every page. Securing this cookie has to wait for
            // the forwarded-headers trust boundary to be decided.
        });

        // Setting to null to allow controller-level request size limits.
        // AddServerHeader off: the banner carries no version, but naming the stack tells a
        // scanner which exploits are worth trying and buys nothing back.
        builder.WebHost.ConfigureKestrel(o =>
        {
            o.Limits.MaxRequestBodySize = null;
            o.AddServerHeader = false;
        });

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
