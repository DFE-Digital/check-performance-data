using System.Globalization;
using DfE.CheckPerformanceData.Application.Media.Serving;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace DfE.CheckPerformanceData.Web.Controllers;

// Serves media library images to public pages, so it is anonymous. A version never changes once
// stored, so every file it serves is cached for a year as immutable.
//
// The id and version decide what is served; the slug and extension are only for people, and a
// URL with different ones is served as it is, not redirected. A resize or crop must carry the
// server's signature (see MediaUrlSigner): without it the answer is 400. An unknown image or
// version is a bare 404, not the site's not-found page.
//
// SVG is served with its own content-security policy, which replaces the site policy set by
// SecurityHeadersMiddleware: no scripts, no loads, and a sandbox, so an SVG opened on its own
// runs nothing. nosniff stops a browser treating any file as another type.
[AllowAnonymous]
[SkipStatusCodePages]
public sealed class MediaFileController(IMediaDeliveryService media) : Controller
{
    private const string LongCache = "public, max-age=31536000, immutable";
    private const string SvgContentType = "image/svg+xml";
    private const string SvgContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'; sandbox";

    // No route constraints: a malformed id or version is answered here with a bare 404, rather
    // than falling through to the content page route.
    [HttpGet("media/{id}/{version}/{file}")]
    public async Task<IActionResult> Get(string id, string version, CancellationToken ct)
    {
        if (!Guid.TryParseExact(id, "D", out var assetId)
            || !int.TryParse(version, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number < 1)
            return NotFound();

        var query = Request.Query.SelectMany(pair => pair.Value.Select(value => new KeyValuePair<string, string?>(pair.Key, value)));
        var result = await media.GetAsync(assetId, number, query, ct);
        Response.Headers.XContentTypeOptions = "nosniff";

        switch (result.Outcome)
        {
            case MediaDeliveryOutcome.NotFound:
                return NotFound();
            case MediaDeliveryOutcome.Invalid:
                return BadRequest();
        }

        Response.Headers.CacheControl = LongCache;
        if (result.ContentType == SvgContentType)
            Response.Headers.ContentSecurityPolicy = SvgContentSecurityPolicy;
        return File(result.Content!, result.ContentType!, lastModified: null, entityTag: EntityTagHeaderValue.Parse(result.ETag));
    }

    // Every other /media path. Requests under /media have no session, so they must not fall
    // through to the content page route, which needs one.
    [HttpGet("media/{**rest}", Order = 1)]
    public IActionResult Other() => NotFound();
}
