using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.Web.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;

namespace DfE.CheckPerformanceData.Web.Controllers;

// Dev-only endpoints that flip the cypd-dev-impersonation cookie so manual testers and
// E2E tests can adopt or shed the editor role without going near the real DfE Sign-In
// flow. The surface is gated on the Dev:ToolsEnabled flag AND a hard production guard —
// mirroring the sibling /dev/* controllers — so it only exists on local dev and ephemeral
// PR/review apps (where the flag is set), never on deployed DEV/QA/Preproduction (where it
// is not) and never in Production. NEVER allow this controller's routes to reach
// production. [AllowAnonymous] is required because the global FallbackPolicy demands
// authentication; E2E callers reach these endpoints before they have any auth cookie.
[AllowAnonymous]
public sealed class DevImpersonationController(IConfiguration configuration, IHostEnvironment env) : Controller
{
    private bool IsAllowed =>
        configuration.GetValue<bool>(SettingKeys.DevToolsEnabled)
        && !env.IsProduction();

    // Accept both verbs so the header link can be a plain <a href> and an E2E client
    // (which would otherwise need to scrape an antiforgery token first) can POST. These
    // endpoints are non-security-sensitive by design: they only flip a dev marker
    // cookie, and prod 404s every request.
    [HttpGet("dev/impersonate/editor")]
    [HttpPost("dev/impersonate/editor")]
    public IActionResult Editor()
    {
        if (!IsAllowed) return NotFound();
        SetCookie(DevImpersonationConstants.EditorValue);
        return RedirectToReferrer();
    }

    [HttpGet("dev/impersonate/user")]
    [HttpPost("dev/impersonate/user")]
    public IActionResult User()
    {
        if (!IsAllowed) return NotFound();
        SetCookie(DevImpersonationConstants.UserValue);
        return RedirectToReferrer();
    }

    [HttpGet("dev/impersonate/admin")]
    [HttpPost("dev/impersonate/admin")]
    public IActionResult Admin()
    {
        if (!IsAllowed) return NotFound();
        SetCookie(DevImpersonationConstants.AdminValue);
        return RedirectToReferrer();
    }

    // Fully clears the dev-impersonation cookie so there's no synthetic principal at
    // all on the next request. Distinct from /user (which keeps the synthetic
    // principal but drops the editor role) because the UI sign-out should make the
    // user genuinely anonymous, not leave a phantom "Dev impersonation user" identity
    // visible in diagnostics. /user stays untouched for E2E tests that rely on its
    // exact toggle semantics.
    // AB#298317: returnUrl lets a caller override the default Referer-based redirect — needed when
    // the Referer is a page that requires authentication (Check your pupil data's "No, I'd like to
    // sign out" answer), which would otherwise bounce the now-signed-out browser straight into a
    // fresh sign-in challenge. Validated as a local URL before use, the same way Controller's own
    // LocalRedirect would, since it arrives as an untrusted query string value.
    [HttpGet("dev/impersonate/clear")]
    [HttpPost("dev/impersonate/clear")]
    public IActionResult Clear(string? returnUrl = null)
    {
        if (!IsAllowed) return NotFound();
        Response.Cookies.Delete(DevImpersonationConstants.CookieName);
        return returnUrl is not null && Url.IsLocalUrl(returnUrl)
            ? Redirect(returnUrl)
            : RedirectToReferrer();
    }

    private void SetCookie(string value)
    {
        Response.Cookies.Append(DevImpersonationConstants.CookieName, value, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Secure = Request.IsHttps,
            // A full working day so a manual testing session doesn't lapse mid-way and bounce the
            // tester to DfE Sign-In (which can't complete on a non-registered local port). Dev-only:
            // this controller 404s in production and E2E sets the cookie fresh each run.
            MaxAge = TimeSpan.FromHours(12)
        });
    }

    private RedirectResult RedirectToReferrer()
    {
        var referrer = Request.Headers["Referer"].ToString();
        return Redirect(string.IsNullOrEmpty(referrer) ? "/" : referrer);
    }
}
