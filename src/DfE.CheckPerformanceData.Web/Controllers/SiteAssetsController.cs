using DfE.CheckPerformanceData.Application.SiteAssets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace DfE.CheckPerformanceData.Web.Controllers;

// Serves the site-wide custom CSS and JavaScript as ordinary same-origin files, so the public
// pages can link them without any inline markup and the content-security policy stays as it is.
// Anonymous because the public pages link them. When an asset is switched off or empty the
// endpoint answers with an empty file rather than an error, so a page cached with an old link
// still loads cleanly.
[AllowAnonymous]
public sealed class SiteAssetsController(ISiteAssetService assets) : Controller
{
    private const string LongCache = "public, max-age=31536000, immutable";

    [HttpGet("cms/custom.css")]
    public async Task<IActionResult> Css(string? v)
    {
        var content = await assets.GetAsync();
        SetCaching(v, content.CssVersion, content.ServesCss);
        return Content(content.ServesCss ? content.Css : "/* no custom CSS */", "text/css; charset=utf-8");
    }

    [HttpGet("cms/custom.js")]
    public async Task<IActionResult> Js(string? v)
    {
        var content = await assets.GetAsync();
        SetCaching(v, content.JsVersion, content.ServesJs);
        return Content(content.ServesJs ? content.Js : "/* no custom JavaScript */", "text/javascript; charset=utf-8");
    }

    // A request that names the time the asset was last saved can be cached for good, because a save
    // changes that time and so the URL. Anything else must be revalidated, and so must
    // an asset that is switched off, so that turning it off takes effect straight away.
    private void SetCaching(string? requestedVersion, string currentVersion, bool serving) =>
        Response.Headers[HeaderNames.CacheControl] =
            serving && currentVersion.Length > 0 && string.Equals(requestedVersion, currentVersion, StringComparison.Ordinal) ? LongCache : "no-cache";
}
