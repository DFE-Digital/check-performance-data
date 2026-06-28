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
    [HttpGet("dev/impersonate/clear")]
    [HttpPost("dev/impersonate/clear")]
    public IActionResult Clear()
    {
        if (!IsAllowed) return NotFound();
        Response.Cookies.Delete(DevImpersonationConstants.CookieName);
        return RedirectToReferrer();
    }

    private void SetCookie(string value)
    {
        Response.Cookies.Append(DevImpersonationConstants.CookieName, value, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Secure = Request.IsHttps,
            MaxAge = TimeSpan.FromHours(1)
        });
    }

    private RedirectResult RedirectToReferrer()
    {
        var referrer = Request.Headers["Referer"].ToString();
        return Redirect(string.IsNullOrEmpty(referrer) ? "/" : referrer);
    }
}
