using DfE.CheckPerformanceData.Application.ContentStaging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
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

            // The ingress is the only path a request can take to reach a pod, so the framework's
            // own "is the immediate peer a trusted proxy?" check is not just redundant, it is
            // wrong here: the ingress is not loopback, so with either trust list left non-empty
            // the edge's X-Forwarded-Proto and X-Forwarded-Host are discarded and Request.IsHttps
            // stays false inside the pod. That dropped header is what produced http:// redirect
            // URIs and cookies without the Secure flag. Trust every peer - which is sound exactly
            // while the network guarantees the only path in is the edge - and revisit this the
            // moment pods become directly reachable (e.g. a Service exposing them without the
            // ingress in front), because then a client could forge the headers.
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
        });

        builder.Services.AddHttpContextAccessor();

        builder.Services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-XSRF-TOKEN";

            // Now that the ingress's X-Forwarded-Proto is trusted (above), Request.IsHttps is
            // true on a deployed pod, so the antiforgery cookie can carry Secure outside
            // Development. It stays SameAsRequest in Development, where the local stack runs over
            // plain HTTP and DefaultAntiforgery.CheckSSLConfig would otherwise reject the token
            // that every page with a form mints.
            options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest
                : CookieSecurePolicy.Always;
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
        })
        // Keep flash messages on the server.
        //
        // MVC defaults to CookieTempDataProvider, which encrypts each TempData payload into
        // .AspNetCore.Mvc.CookieTempDataProvider (chunked across as many 4 KB cookies as it
        // takes) and has the browser present it back on every subsequent request to the site.
        // That turns a banner into a request header, and request headers are budgeted by the
        // reverse proxy in front of the pod, not by Kestrel: past the proxy's buffer it answers
        // 400 "Request Header Or Cookie Too Large" itself, so the app never sees the request and
        // cannot clear the cookie that caused it. Every page then fails until the user clears
        // their cookies by hand. A signed-in user already carries a chunked ~4 KB authentication
        // cookie, so a couple of kilobytes of import-result banner is enough to cross the line —
        // which is exactly how a content-staging import took an environment out.
        //
        // The session store is Postgres-backed and shared by every replica (AddCpdSessionStore),
        // so server-side TempData survives a redirect that load-balances to another pod just as
        // reliably as the cookie did, costs the client nothing, and removes the size ceiling
        // from every banner in the app rather than one caller at a time.
        .AddSessionStateTempDataProvider();

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
