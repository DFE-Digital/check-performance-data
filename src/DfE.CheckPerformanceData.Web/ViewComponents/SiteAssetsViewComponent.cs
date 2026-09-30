using DfE.CheckPerformanceData.Application.SiteAssets;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.ViewComponents;

// The links to the site-wide custom CSS and JavaScript, or nothing at all where they must not apply.
public sealed record SiteAssetLinks(string? CssUrl, string? JsUrl)
{
    public static readonly SiteAssetLinks None = new(null, null);

    // The admin area never gets the assets: a broken script there could stop an administrator
    // reaching the very page that switches it off. ?siteAssets=off is a safe mode for any public
    // page, so an administrator can see a page without the assets while diagnosing a problem.
    public static SiteAssetLinks For(SiteAssetContent content, PathString path, QueryString query)
    {
        if (path.StartsWithSegments("/admin", StringComparison.OrdinalIgnoreCase))
            return None;

        if (Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(query.Value).TryGetValue("siteAssets", out var mode)
            && string.Equals(mode.ToString(), "off", StringComparison.OrdinalIgnoreCase))
            return None;

        return new SiteAssetLinks(
            content.ServesCss ? $"/cms/site.css?v={content.CssVersion}" : null,
            content.ServesJs ? $"/cms/site.js?v={content.JsVersion}" : null);
    }
}

public sealed class SiteAssetsViewComponent(ISiteAssetService assets) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        var links = SiteAssetLinks.For(await assets.GetAsync(), HttpContext.Request.Path, HttpContext.Request.QueryString);
        return View(links);
    }
}
