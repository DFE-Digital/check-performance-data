using DfE.CheckPerformanceData.Application.Settings;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DfE.CheckPerformanceData.Web.Extensions;

// Session-cookie configuration is a single-source-of-truth extension so the tests and
// Program.cs both exercise the same binding. The idle timeout is settings-driven so
// support-team-quotable session lifetimes can be tuned without a redeploy; HttpOnly +
// IsEssential are held constant because those are non-negotiable and never depend on
// config.
//
// SecurePolicy: Always in every non-Development environment so a replayed http request
// against a staging/production host cannot leak the cookie. In Development we downgrade
// to SameAsRequest so plain-http local dev (docker compose exposing :8080) can round-trip
// the cookie for E2E browsers hitting the container over http (e.g. via
// host.docker.internal, which does NOT get Chromium's localhost secure-context exemption).
// The dev downgrade is safe because Development environments never serve real user data.
//
// Cookie.MaxAge is deliberately NOT set. A browser-side MaxAge only tells the client
// when to stop presenting the cookie; a replayed cookie past that expiry still hits a
// live server-side session that a sliding idle timeout would keep refreshing. The
// absolute lifetime cap is enforced server-side by SessionAbsoluteLifetimeMiddleware.
public static class SessionServiceCollectionExtensions
{
    // Configuration-only: SessionOptions is read once when the pipeline is built, so this
    // cannot be an editable admin setting (see SettingDefinitions). One source of truth
    // for the key name even so.
    public const string IdleMinutesKey = SettingKeys.SearchAnalyticsSessionIdleMinutes;
    public const int DefaultIdleMinutes = 60;

    public static IServiceCollection AddCpdSession(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        var idleMinutes = configuration.GetValue<int?>(IdleMinutesKey) ?? DefaultIdleMinutes;
        var securePolicy = environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;

        services.AddSession(options =>
        {
            options.IdleTimeout = TimeSpan.FromMinutes(idleMinutes);
            options.Cookie.HttpOnly = true;
            options.Cookie.IsEssential = true;
            options.Cookie.SecurePolicy = securePolicy;
        });

        return services;
    }
}
