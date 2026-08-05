using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.Web.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace DfE.CheckPerformanceData.Web.Startup;

public static class DevImpersonationExtensions
{
    public static WebApplicationBuilder AddCpdDevImpersonation(this WebApplicationBuilder builder)
    {
        // Dev-only impersonation: a second auth scheme + a policy scheme that picks between
        // it and the real DfE cookie scheme based on which cookie is present. Registered
        // ONLY where the dev tooling surface is enabled (Dev:ToolsEnabled) — i.e. local dev
        // and ephemeral PR/review apps — AND never in Production. This matches the gate used
        // by the sibling /dev/* controllers (DevQueueSeed, DevPipeline, DevUat), so deployed
        // DEV/QA/Preproduction (which never set Dev:ToolsEnabled) cannot serve these routes or
        // carry the marker cookie. The IsProduction() guard is belt-and-braces on top of the
        // flag. Don't move this block outside either condition.
        if (!builder.Environment.IsProduction()
            && builder.Configuration.GetValue<bool>(SettingKeys.DevToolsEnabled))
        {
            const string DevAwareScheme = "DevAware";

            builder.Services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, DevImpersonationAuthHandler>(
                    DevImpersonationConstants.Scheme,
                    _ => { })
                .AddPolicyScheme(DevAwareScheme, DevAwareScheme, options =>
                {
                    options.ForwardDefaultSelector = context =>
                    {
                        // Prefer the real DfE Sign-In auth cookie if present so an
                        // already-signed-in manual tester keeps their real claims
                        // (organisationid, name, etc.) when they flip the impersonation
                        // cookie. The transformer then overlays the editor role on top.
                        // Only fall back to the synthetic DevImpersonation scheme when
                        // there's no real session — the E2E case.
                        if (context.Request.Cookies.ContainsKey(".AspNetCore.Cookies"))
                            return CookieAuthenticationDefaults.AuthenticationScheme;
                        if (context.Request.Cookies.ContainsKey(DevImpersonationConstants.CookieName))
                            return DevImpersonationConstants.Scheme;
                        return CookieAuthenticationDefaults.AuthenticationScheme;
                    };
                });

            // Override only DefaultAuthenticateScheme so [Authorize] checks pass through
            // the policy scheme. DefaultChallengeScheme stays as OpenIdConnect so DfE
            // Sign-In still triggers for unauthenticated users; DefaultSignInScheme stays
            // as Cookies so the OIDC callback still writes the real auth cookie.
            builder.Services.Configure<AuthenticationOptions>(options =>
            {
                options.DefaultAuthenticateScheme = DevAwareScheme;
            });

            builder.Services.AddScoped<IClaimsTransformation, DevImpersonationClaimsTransformer>();
        }

        return builder;
    }
}
