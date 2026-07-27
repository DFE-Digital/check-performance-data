using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DfE.CheckPerformanceData.Web.Extensions;

// Session-cookie configuration is a single-source-of-truth extension so the tests and
// Program.cs both exercise the same binding. The idle timeout is settings-driven so
// support-team-quotable session lifetimes can be tuned without a redeploy; the cookie
// hardening bits (HttpOnly / IsEssential / Secure) are held constant because those
// are non-negotiable and never depend on config.
//
// Cookie.MaxAge is deliberately NOT set. A browser-side MaxAge only tells the client
// when to stop presenting the cookie; a replayed cookie past that expiry still hits a
// live server-side session that a sliding idle timeout would keep refreshing. The
// absolute lifetime cap is enforced server-side by SessionAbsoluteLifetimeMiddleware.
public static class SessionServiceCollectionExtensions
{
    public const string IdleMinutesKey = "SearchAnalytics:SessionIdleMinutes";
    public const int DefaultIdleMinutes = 60;

    public static IServiceCollection AddCpdSession(this IServiceCollection services, IConfiguration configuration)
    {
        var idleMinutes = configuration.GetValue<int?>(IdleMinutesKey) ?? DefaultIdleMinutes;

        services.AddSession(options =>
        {
            options.IdleTimeout = TimeSpan.FromMinutes(idleMinutes);
            options.Cookie.HttpOnly = true;
            options.Cookie.IsEssential = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        });

        return services;
    }
}
