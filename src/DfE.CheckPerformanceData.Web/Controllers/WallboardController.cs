using DfE.CheckPerformanceData.Application.Observability;
using DfE.CheckPerformanceData.Web.Models.Observability;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

// The wallboard: a chrome-less, large-type, auto-refreshing rendering variant of the SAME
// aggregate-only data contract as the share link, behind the SAME opaque-token gate. It is
// [AllowAnonymous] at the class level (reachable without the admin cookie) but every action first
// validates a wallboard-scoped token; a missing, invalid or revoked token returns 404 — never 401,
// redirect or challenge. It serves the aggregate-only AggregateShareViewModel (zero pupil
// identifiers by construction) and carries no admin actions and no mutating verbs — read-only, GET
// only. A token issued for the share link does not validate here and vice versa.
[AllowAnonymous]
public sealed class WallboardController : Controller
{
    private readonly IShareTokenService _tokens;
    private readonly IMetricsQueryService _query;

    public WallboardController(IShareTokenService tokens, IMetricsQueryService query)
    {
        _tokens = tokens;
        _query = query;
    }

    // The URL carries a live bearer token, so the response must never be cached by the browser or
    // a shared proxy (otherwise a revoked token's page could keep rendering from cache), and the
    // referrer must not leak the tokened URL to any link target.
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    [HttpGet("wallboard/{token}")]
    public async Task<IActionResult> Index(string token, CancellationToken cancellationToken = default)
    {
        if (!await _tokens.ValidateAsync(token, ShareSurface.Wallboard, cancellationToken))
            return NotFound();

        // Defence-in-depth for the token-in-URL design: never leak the tokened URL as a referrer.
        if (HttpContext is not null)
            Response.Headers["Referrer-Policy"] = "no-referrer";

        var snapshot = await _query.GetAggregateShareAsync(cancellationToken);
        return View(AggregateShareViewModel.From(snapshot));
    }
}
