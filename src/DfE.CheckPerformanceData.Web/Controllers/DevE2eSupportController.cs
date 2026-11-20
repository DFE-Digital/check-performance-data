using System.Text;
using System.Text.Encodings.Web;
using DfE.CheckPerformanceData.Application.Media.Serving;
using DfE.CheckPerformanceData.Application.Media.Upload;
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
//   * POST /dev/media/sample?kind=png|svg — puts a fixed sample image in the media library through
//     the real upload pipeline and returns its id, version, URL and signed srcset. The same file each time, so a
//     second call returns the image the first one made.
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

    // A 64 by 32 PNG, green on the left and blue on the right.
    private const string SamplePngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAEAAAAAgCAIAAAAt/+nTAAAAPUlEQVR42u3PMREAMAgEsDeCk4pCHvLQ0IEtdzGQpN+p6jkVAQEBAQEBAQEBAQEBAQEBAQEBAQEBAYHfwAKylcRqbBIuxAAAAABJRU5ErkJggg==";

    private const string SampleSvg =
        "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"32\" viewBox=\"0 0 64 32\">"
        + "<rect width=\"32\" height=\"32\" fill=\"#00703c\"/><rect x=\"32\" width=\"32\" height=\"32\" fill=\"#1d70b8\"/></svg>";

    [HttpPost("dev/media/sample")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MediaSample(
        string? kind,
        [FromServices] IMediaUploadService uploads,
        [FromServices] IMediaUrlBuilder urls,
        CancellationToken ct)
    {
        if (!IsAllowed) return NotFound();
        (byte[] Bytes, string Name)? sample = kind switch
        {
            "png" => (Convert.FromBase64String(SamplePngBase64), "sample.png"),
            "svg" => (Encoding.UTF8.GetBytes(SampleSvg), "sample.svg"),
            _ => null
        };
        if (sample is null) return BadRequest();

        var result = await uploads.UploadAsync(new MemoryStream(sample.Value.Bytes), sample.Value.Name, ct);
        if (result.Asset is not { } asset) return UnprocessableEntity(result.Rejection);
        return Json(new { asset.Id, Version = asset.CurrentVersion, Url = urls.Original(asset, asset.Latest), SrcSet = urls.SrcSet(asset, asset.Latest) });
    }
}
