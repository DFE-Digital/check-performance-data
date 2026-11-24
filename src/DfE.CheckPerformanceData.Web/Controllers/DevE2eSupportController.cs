using System.Text;
using System.Text.Encodings.Web;
using DfE.CheckPerformanceData.Application.Media;
using DfE.CheckPerformanceData.Application.Media.Serving;
using DfE.CheckPerformanceData.Application.Media.Upload;
using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;
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
//     second call returns the image the first one made. An optional alt= sets the image's alt text,
//     so the library's "Missing alt text" filter has an image to leave out.
//   * POST /dev/media/usage?mediaId=&pageId= — records that the page's latest version uses the
//     image's current version, so the library's delete guard and where-used list have a usage.
//   * POST /dev/media/usage/clear?mediaId= — removes every recorded use of the image, so a test
//     can delete the image when it ends.
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
        string? alt,
        [FromServices] IMediaUploadService uploads,
        [FromServices] IMediaUrlBuilder urls,
        [FromServices] IMediaAssetRepository assets,
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

        var result = await uploads.UploadAsync(new MemoryStream(sample.Value.Bytes), sample.Value.Name, ct: ct);
        if (result.Asset is not { } asset) return UnprocessableEntity(result.Rejection);
        if (!string.IsNullOrWhiteSpace(alt))
            await assets.UpdateMetadataAsync(asset.Id, new MediaAssetMetadata(alt, asset.Caption, asset.Credit, asset.Tags), ct);
        return Json(new { asset.Id, Version = asset.CurrentVersion, Url = urls.Original(asset, asset.Latest), SrcSet = urls.SrcSet(asset, asset.Latest) });
    }



    [HttpPost("dev/media/usage")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MediaUsage(Guid mediaId, Guid pageId, [FromServices] IPortalDbContext db, CancellationToken ct)
    {
        if (!IsAllowed) return NotFound();
        var version = await db.MediaAssets.AsNoTracking()
            .Where(a => a.Id == mediaId).Select(a => (int?)a.CurrentVersion).FirstOrDefaultAsync(ct);
        var pageVersion = await db.PageNodeVersions.AsNoTracking()
            .Where(v => v.PageNodeId == pageId)
            .OrderByDescending(v => v.VersionId)
            .Select(v => (Guid?)v.Id)
            .FirstOrDefaultAsync(ct);
        if (version is null || pageVersion is null) return NotFound();

        db.MediaUsages.Add(new MediaUsage
        {
            Id = Guid.NewGuid(),
            AssetId = mediaId,
            AssetVersion = version.Value,
            PageNodeId = pageId,
            PageNodeVersionId = pageVersion.Value,
            Location = MediaUsageLocations.Widget,
            WidgetId = "0",
        });
        await db.SaveChangesAsync(ct);
        return Ok();
    }

    [HttpPost("dev/media/usage/clear")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ClearMediaUsages(Guid mediaId, [FromServices] IPortalDbContext db, CancellationToken ct)
    {
        if (!IsAllowed) return NotFound();
        var removed = await db.MediaUsages.Where(u => u.AssetId == mediaId).ExecuteDeleteAsync(ct);
        return Json(new { removed });
    }
}
