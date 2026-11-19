using System.Text.Encodings.Web;
using System.Security.Claims;
using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.Web.Impersonation;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace DfE.CheckPerformanceData.Web.Controllers;

// Test-only surface for the E2E suite. Editor-gated AND double-gated on
// Dev:ToolsEnabled + !IsProduction (mirroring DevImpersonationController and the other
// sibling Dev*Controllers) so it 404s on any real environment.
//
//   * GET /dev/antiforgery-token — issues the antiforgery cookie + a lone <input>
//     containing the matching token, so seed helpers can POST to editor-gated
//     endpoints (e.g. /content-block/save) without scraping a real form.
[Authorize(Roles = WikiConstants.EditorRole)]
public sealed class DevE2eSupportController(
    IConfiguration configuration,
    IHostEnvironment env) : Controller
{
    private bool IsAllowed =>
        configuration.GetValue<bool>(SettingKeys.DevToolsEnabled)
        && !env.IsProduction();

    [HttpGet("dev/e2e/admin-session")]
    public async Task<IActionResult> AdminSession()
    {
        if (!IsAllowed || !configuration.GetValue<bool>("E2E:TestAdminAuthenticationEnabled"))
            return NotFound();

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "00000000-0000-0000-0000-000000000002"),
            new Claim(ClaimTypes.GivenName, "E2E"),
            new Claim(ClaimTypes.Surname, "Administrator"),
            new Claim("organisation_id", "e2e-admin"),
            new Claim("organisation_name", "E2E Administrator"),
            new Claim("organisation_urn", string.Empty),
            new Claim("organisation_laestab", string.Empty),
            new Claim("organisation_type_id", string.Empty),
            new Claim(ClaimTypes.Role, ImpersonationAccessPolicy.AdminRole),
            new Claim(ClaimTypes.Role, WikiConstants.EditorRole)
        };

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            claims, CookieAuthenticationDefaults.AuthenticationScheme));
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
        return Redirect("/admin/impersonation");
    }

    [HttpGet("dev/antiforgery-token")]
    public ContentResult AntiforgeryToken([FromServices] IAntiforgery antiforgery)
    {
        if (!IsAllowed) { Response.StatusCode = 404; return Content(string.Empty); }
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        var encoded = HtmlEncoder.Default.Encode(tokens.RequestToken ?? string.Empty);
        return Content(
            $"<input name=\"__RequestVerificationToken\" type=\"hidden\" value=\"{encoded}\" />",
            "text/html");
    }
}
