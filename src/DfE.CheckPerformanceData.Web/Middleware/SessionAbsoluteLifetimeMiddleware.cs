using System.Globalization;
using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.Web.Session;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DfE.CheckPerformanceData.Web.Middleware;

// Enforces a server-side ceiling on session lifetime that the sliding IdleTimeout does
// not touch. Cookie.MaxAge is deliberately unset in the AddSession config because a
// browser-side MaxAge only tells the client when to stop presenting the cookie — a
// replayed cookie past that expiry still hits a live server-side session that the
// idle timeout would keep refreshing forever. The absolute cap has to be enforced
// here.
//
// This middleware ALSO has a load-bearing side effect: writing _sessionStartedAtUtc
// through Session.SetString is what materialises the ASP.NET session cookie in the
// first place. Reading Session.Id without any write is a no-op — the framework
// lazy-writes on first store mutation, so without this middleware, downstream code
// that reads the session would see a fresh id on every request. Placing this middleware
// immediately after UseSession() gives every downstream consumer (feedback form,
// source-comment injector, sink emitter) a stable session identity to work with.
//
// That identity is CpdSessionIdentity, not ASP.NET's Session.Id, precisely so the cap
// can rotate it — the framework id is cookie-derived and survives Session.Clear().
public sealed class SessionAbsoluteLifetimeMiddleware
{
    private const string StartKey = "_sessionStartedAtUtc";
    private const string ConfigKey = "SearchAnalytics:SessionAbsoluteHours";
    private const double DefaultAbsoluteHours = 24.0;
    private const double MinAbsoluteHours = 0.0001;
    private const double MaxAbsoluteHours = 168.0;

    private readonly RequestDelegate _next;
    private readonly IConfiguration _configuration;

    public SessionAbsoluteLifetimeMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        _configuration = configuration;
    }

    // Public + static so a unit test can pin the clamp without booting the full pipeline.
    public static double ResolveAbsoluteHours(double configured) =>
        Math.Clamp(configured, MinAbsoluteHours, MaxAbsoluteHours);

    // The cap is an editable admin setting, so the stored value has to win over
    // appsettings — otherwise the settings page saves and displays a value that never
    // takes effect. ISettingService is scoped, hence the per-request resolve; it is
    // absent in bare middleware test hosts and in any pipeline composed without the
    // application services, so fall back to configuration rather than hard-failing.
    private async Task<double> ReadConfiguredHoursAsync(HttpContext context)
    {
        var settings = context.RequestServices?.GetService<ISettingService>();
        if (settings is not null)
        {
            return await settings.GetDoubleAsync(SettingKeys.SearchAnalyticsSessionAbsoluteHours);
        }

        return _configuration.GetValue<double?>(ConfigKey) ?? DefaultAbsoluteHours;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        await context.Session.LoadAsync();

        var absoluteHours = ResolveAbsoluteHours(await ReadConfiguredHoursAsync(context));
        var cap = TimeSpan.FromHours(absoluteHours);
        var now = DateTime.UtcNow;

        var startedStr = context.Session.GetString(StartKey);
        if (startedStr is not null
            && DateTime.TryParse(
                   startedStr,
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.RoundtripKind,
                   out var started)
            && now - started >= cap)
        {
            // Past the absolute cap even under continuous activity — wipe the session and
            // start over. Clearing also discards the analytics identity, so the Ensure
            // below mints a fresh one: that is what makes the cutoff observable downstream.
            // (Session.Id itself cannot rotate — see CpdSessionIdentity.)
            context.Session.Clear();
            startedStr = null;
        }

        if (startedStr is null)
        {
            // First access for this session, or the first access after a cap wipe: stamp
            // the start time. This SetString is what commits the session cookie on the
            // response — see the class docstring.
            context.Session.SetString(StartKey, now.ToString("O", CultureInfo.InvariantCulture));
        }

        // Establish (or re-establish) the app-owned analytics identity every request. It is
        // a no-op once stored, so the cost is a dictionary lookup on the loaded session.
        CpdSessionIdentity.Ensure(context.Session);

        await _next(context);
    }
}
