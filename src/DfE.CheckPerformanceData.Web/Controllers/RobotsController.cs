using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;

namespace DfE.CheckPerformanceData.Web.Controllers;

// Served from a controller rather than wwwroot so the content follows the host environment.
//
// Second layer behind the X-Robots-Tag header that SecurityHeadersMiddleware sets on every
// non-production response (#444). The header removes pages already in the index; this stops a
// compliant crawler fetching them again. On its own it would be worse than nothing — a crawler
// told not to fetch a page never sees the noindex on it, so a stale entry stays in the index.
//
// Production stays crawlable (the public content and guidance pages are meant to be found) but
// opts out of AI training crawlers: the service is behind DfE Sign-in and holds nothing a
// training corpus should have. A disallow is the accepted control; a crawler that ignores it
// needs a WAF rule, not more robots.txt.
//
// [AllowAnonymous] because the global FallbackPolicy demands a signed-in user, and a robots.txt
// behind a sign-in redirect is a robots.txt no crawler ever reads.
[AllowAnonymous]
public sealed class RobotsController(IHostEnvironment environment) : Controller
{
    private const string HideEverything =
        "User-agent: *\n" +
        "Disallow: /\n";

    private const string AllowExceptTrainingCrawlers =
        "User-agent: GPTBot\n" +
        "Disallow: /\n" +
        "\n" +
        "User-agent: ClaudeBot\n" +
        "Disallow: /\n" +
        "\n" +
        "User-agent: Google-Extended\n" +
        "Disallow: /\n" +
        "\n" +
        "User-agent: CCBot\n" +
        "Disallow: /\n" +
        "\n" +
        "User-agent: *\n" +
        "Allow: /\n";

    [HttpGet("/robots.txt")]
    public IActionResult Index() =>
        Content(environment.IsProduction() ? AllowExceptTrainingCrawlers : HideEverything, "text/plain");
}
