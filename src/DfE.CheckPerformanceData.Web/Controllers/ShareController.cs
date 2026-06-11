using DfE.CheckPerformanceData.Application.Observability;
using DfE.CheckPerformanceData.Web.Models.Observability;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

// The anonymised, read-only stakeholder share link. This is a deliberate "show without admin"
// surface: it is [AllowAnonymous] at the class level so it is reachable without the cypmd_admin
// cookie, but it is gated by an opaque, revocable share token in the URL path. Every action first
// validates the token; a missing, invalid or revoked token returns 404 — never 401, never a
// redirect, never an OIDC challenge — so an uninvited viewer is not bounced into the DfE sign-in
// flow (which would confirm the surface exists and leak the auth topology).
//
// The view it serves is the aggregate-only AggregateShareViewModel, built from the aggregate-only
// query projection. It is NOT the authenticated DashboardViewModel: the share surface carries zero
// pupil identifiers by construction, not by redaction-at-render. There are no admin actions and no
// mutating verbs here — read-only, GET only.
[AllowAnonymous]
public sealed class ShareController : Controller
{
    private readonly IShareTokenService _tokens;
    private readonly IMetricsQueryService _query;

    public ShareController(IShareTokenService tokens, IMetricsQueryService query)
    {
        _tokens = tokens;
        _query = query;
    }

    // The URL carries a live bearer token, so the response must never be cached by the browser or
    // a shared proxy (otherwise a revoked token's page could keep rendering from cache), and the
    // referrer must not leak the tokened URL to any link target.
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    [HttpGet("share/{token}")]
    public async Task<IActionResult> Index(string token, CancellationToken cancellationToken = default)
    {
        if (!await _tokens.ValidateAsync(token, ShareSurface.Share, cancellationToken))
            return NotFound();

        // Defence-in-depth for the token-in-URL design: never leak the tokened URL as a referrer.
        if (HttpContext is not null)
            Response.Headers["Referrer-Policy"] = "no-referrer";

        var snapshot = await _query.GetAggregateShareAsync(cancellationToken);
        return View(AggregateShareViewModel.From(snapshot));
    }
}
