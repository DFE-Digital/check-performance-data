using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

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
// that reads Session.Id would see a fresh id on every request. Placing this middleware
// immediately after UseSession() gives every downstream consumer (feedback form,
// source-comment injector, sink emitter) a stable session identity to work with.
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

    public async Task InvokeAsync(HttpContext context)
    {
        await context.Session.LoadAsync();

        var configured = _configuration.GetValue<double?>(ConfigKey) ?? DefaultAbsoluteHours;
        var absoluteHours = ResolveAbsoluteHours(configured);
        var cap = TimeSpan.FromHours(absoluteHours);
        var now = DateTime.UtcNow;

        var startedStr = context.Session.GetString(StartKey);
        if (startedStr is null)
        {
            // First access for this session: stamp the start time. This SetString is
            // what commits the session cookie on the response — see the class docstring.
            context.Session.SetString(StartKey, now.ToString("O", CultureInfo.InvariantCulture));
        }
        else if (DateTime.TryParse(
                     startedStr,
                     CultureInfo.InvariantCulture,
                     DateTimeStyles.RoundtripKind,
                     out var started)
                 && now - started >= cap)
        {
            // Past the absolute cap even under continuous activity — wipe the session
            // and start over. The next request under the fresh cookie will re-stamp
            // the start key at the top of this method.
            context.Session.Clear();
            context.Session.SetString(StartKey, now.ToString("O", CultureInfo.InvariantCulture));
        }

        await _next(context);
    }
}
