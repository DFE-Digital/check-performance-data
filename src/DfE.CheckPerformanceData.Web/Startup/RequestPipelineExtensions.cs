using DfE.CheckPerformanceData.Web.Diagnostics;
using DfE.CheckPerformanceData.Web.Middleware;
using GovUk.Frontend.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Serilog;

namespace DfE.CheckPerformanceData.Web.Startup;

public static class RequestPipelineExtensions
{
    // ORDER IS LOAD-BEARING throughout this method — every placement comment moved from
    // Program.cs documents a real constraint. Do not reorder without reading them.
    public static WebApplication UseCpdRequestPipeline(this WebApplication app)
    {
        app.UseForwardedHeaders();

        app.UseSerilogRequestLogging(options =>
        {
            options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
            {
                diagnosticContext.Set("RequestPath", httpContext.Request.Path);
                diagnosticContext.Set("StatusCode", httpContext.Response.StatusCode);
                diagnosticContext.Set("RequestMethod", httpContext.Request.Method);
                diagnosticContext.Set("UserAgent", httpContext.Request.Headers.UserAgent.ToString());
            };
        });

        app.UseGovUkFrontend();

        app.MapHealthChecks("/healthcheck").AllowAnonymous();

        // Configure the HTTP request pipeline.
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Home/Error");
            // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
            app.UseHsts();
        }

        app.UseHttpsRedirection();

        app.UseCpdContentSecurityPolicy();

        app.UseSession();

        // Sits immediately after UseSession() so it can call Session.LoadAsync(); its
        // SetString on first access is what commits the session cookie (framework
        // lazy-writes on first store mutation). Downstream consumers therefore see a
        // stable Session.Id across requests. Also enforces the server-side absolute
        // lifetime cap that Cookie.MaxAge (a browser-side hint only) cannot.
        app.UseMiddleware<SessionAbsoluteLifetimeMiddleware>();

        app.UseRouting();

        // Re-executes unmapped-route 404 responses through the MVC pipeline so users see a
        // text/html page (with the shared layout, and therefore the injected session
        // comment) instead of the framework default text/plain "Status Code: 404" body.
        // Sits after UseRouting per the framework's placement contract for status-code
        // page middleware.
        app.UseStatusCodePagesWithReExecute("/Home/NotFound");

        app.UseAuthentication();
        app.UseAuthorization();

        app.UseCpdBigQueryAnalytics();

        // Sits after auth so the diagnostic comment sees the final principal claims;
        // before controllers so it can wrap their response body. The middleware itself
        // is a no-op when env.IsProduction() or when Diagnostics:ShowSessionFooter
        // is false / unset.
        app.UseMiddleware<DiagnosticFooterMiddleware>();

        // Emits `<!-- session: {id} -->` before </body> on every text/html response so
        // users can quote the session id back to support. Placed after auth so any
        // future admin-only variants would still see the right principal; placed after
        // SessionAbsoluteLifetimeMiddleware so Session.Id is stable (cookie committed).
        app.UseMiddleware<SessionSourceCommentMiddleware>();

        app.MapStaticAssets().AllowAnonymous();

        app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}")
            .WithStaticAssets();

        return app;
    }

    private static void UseCpdContentSecurityPolicy(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            context.Response.Headers.Append("Content-Security-Policy",
                "default-src 'self'; " +
                "script-src 'self' 'unsafe-inline' 'unsafe-eval' https://*.googletagmanager.com https://*.google-analytics.com https://*.analytics.google.com https://*.clarity.ms; " +
                "style-src 'self' 'unsafe-inline' https://*.googletagmanager.com https://fonts.googleapis.com; " +
                "img-src 'self' data: blob: https://*.googletagmanager.com https://*.google-analytics.com https://*.analytics.google.com https://*.clarity.ms https://fonts.gstatic.com; " +
                "font-src 'self' data: https://fonts.gstatic.com; " +
                "connect-src 'self' https://*.googletagmanager.com https://*.google-analytics.com https://*.analytics.google.com https://*.clarity.ms; " +
                "frame-src 'self' https://*.googletagmanager.com; " +
                "object-src 'none'; " +
                "base-uri 'self'; " +
                "form-action 'self'");
            await next();
        });
    }
}
