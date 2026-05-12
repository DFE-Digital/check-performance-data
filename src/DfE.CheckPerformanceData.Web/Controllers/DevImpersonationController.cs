using DfE.CheckPerformanceData.Web.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;

namespace DfE.CheckPerformanceData.Web.Controllers;

// Dev-only endpoints that flip the cypd-dev-impersonation cookie so manual testers and
// E2E tests can adopt or shed the editor role without going near the real DfE Sign-In
// flow. Every action returns 404 when the host environment is Production — belt-and-
// braces alongside Program.cs's gated DI registration. NEVER allow this controller's
// routes to reach production. [AllowAnonymous] is required because the global
// FallbackPolicy demands authentication; E2E callers reach these endpoints before they
// have any auth cookie.
[AllowAnonymous]
public sealed class DevImpersonationController(IHostEnvironment env) : Controller
{
    private bool IsAllowed => !env.IsProduction();

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
