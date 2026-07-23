using System.Text.Encodings.Web;
using DfE.CheckPerformanceData.Application.Settings;
using Microsoft.AspNetCore.Antiforgery;
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
