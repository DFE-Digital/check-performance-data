using DfE.CheckPerformanceData.Web.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;

namespace DfE.CheckPerformanceData.Web.Controllers;

// Dev-only endpoints that flip the cypd-dev-impersonation cookie so manual testers and
// E2E tests can adopt or shed the editor role without going near the real DfE Sign-In
// flow. Every action returns 404 when the host environment is Production — belt-and-
// braces alongside Program.cs's gated DI registration. NEVER allow this controller's
// routes to reach production.
public sealed class DevImpersonationController(IHostEnvironment env) : Controller
{
    private bool IsAllowed => !env.IsProduction();

    [HttpPost("dev/impersonate/editor")]
    public IActionResult Editor()
    {
        if (!IsAllowed) return NotFound();
        SetCookie(DevImpersonationConstants.EditorValue);
        return RedirectToReferrer();
    }

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
