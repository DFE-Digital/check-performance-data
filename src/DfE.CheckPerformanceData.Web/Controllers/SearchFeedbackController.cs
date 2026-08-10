using System.Globalization;
using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using DfE.CheckPerformanceData.Web.Session;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

// User-facing feedback surface. Users land here from the "Not the results you were
// expecting?" callout on /search when they can't find what they wanted; the form gives
// them a way to send a note keyed by their session id so support can look up the actual
// searches they ran.
//
// Two invariants carry this controller:
//   - The server-side CpdSessionIdentity is the ONLY source of the persisted session id.
//     Any form field the client sends under the SessionIdDisplayOnly name is discarded.
//     The readonly input on the form exists only so the user can quote the id.
//   - Hide-my-email means DROP the value before persist. There is no encryption, no
//     reveal audit — what isn't stored can't leak.
[AllowAnonymous]
[Route("/Search/Feedback")]
public sealed class SearchFeedbackController : Controller
{
    // TempData key carrying the just-inserted message's session id from POST to the
    // confirmation GET so the user sees the exact id we wrote for their record.
    private const string ConfirmationSessionIdKey = "FeedbackSessionId";

    private readonly ISearchMessageService _messages;
    private readonly ISearchAnalyticsQueryService _query;
    private readonly ICurrentUserService? _currentUser;

    public SearchFeedbackController(
        ISearchMessageService messages,
        ISearchAnalyticsQueryService query,
        ICurrentUserService? currentUser = null)
    {
        _messages = messages;
        _query = query;
        _currentUser = currentUser;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        await HttpContext.Session.LoadAsync(ct);
        var sessionId = CpdSessionIdentity.Ensure(HttpContext.Session);

        // The prior-search block is best-effort: if the query service returns null (no
        // matching row for the session), the form still renders without the panel.
        var latest = await _query.GetLatestSearchForSessionAsync(sessionId, ct);
        var priorSearch = latest is null ? null : ToDisplay(latest);

        var model = new SearchFeedbackViewModel
        {
            SessionId = sessionId,
            PriorSearchPrefill = latest is null ? null : FormatPrefill(latest),
            PriorSearch = priorSearch,
            // Pre-fill from the signed-in user's email claim when present so the user does
            // not have to retype their address. Anonymous users see an empty field. The
            // user can clear the value (or tick "Hide my email") — both flows still work.
            Email = ResolveAutoFillEmail(),
        };
        return View("~/Views/Search/Feedback.cshtml", model);
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(SearchFeedbackViewModel form, CancellationToken ct)
    {
        await HttpContext.Session.LoadAsync(ct);
        // Server-side session id — IGNORES any SessionIdDisplayOnly form field the client
        // sent. The view-model has no such property; even if the browser posted one under
        // that name the model binder has nowhere to put it.
        var sessionId = CpdSessionIdentity.Ensure(HttpContext.Session);

        if (!ModelState.IsValid)
        {
            // Re-fetch the prior-search panel + the auto-filled email so the redisplay
            // renders with the same context the user saw before submitting. Without this
            // the page shrinks to just the form on error — a bad surprise when the user
            // has to correct a typo.
            var latest = await _query.GetLatestSearchForSessionAsync(sessionId, ct);
            var priorSearch = latest is null ? null : ToDisplay(latest);
            var autoFilledEmail = ResolveAutoFillEmail();

            var redisplay = new SearchFeedbackViewModel
            {
                SessionId = sessionId,
                PriorSearchPrefill = null,
                PriorSearch = priorSearch,
                WhatLookingFor = form.WhatLookingFor,
                WhatGot = form.WhatGot,
                // User-typed value wins; fall back to auto-fill so an empty POST still
                // resurfaces the signed-in user's email on redisplay.
                Email = string.IsNullOrWhiteSpace(form.Email) ? autoFilledEmail : form.Email,
                HideMyEmail = form.HideMyEmail,
            };
            return View("~/Views/Search/Feedback.cshtml", redisplay);
        }

        // Hide-my-email is the single privacy control. When ticked, drop the value BEFORE
        // it reaches the message-service so the persisted row's email column is NULL.
        var email = form.HideMyEmail
            ? null
            : string.IsNullOrWhiteSpace(form.Email) ? null : form.Email.Trim();

        var whatGot = string.IsNullOrWhiteSpace(form.WhatGot) ? null : form.WhatGot.Trim();
        var whatLookingFor = (form.WhatLookingFor ?? string.Empty).Trim();

        await _messages.CreateAsync(sessionId, whatLookingFor, whatGot, email, ct);

        // Carry the id through TempData so the confirmation view can show it. A refresh
        // of the confirmation page loses the display gracefully — TempData is
        // single-request by design.
        TempData[ConfirmationSessionIdKey] = sessionId;
        return RedirectToAction(nameof(Confirmation));
    }

    [HttpGet("Confirmation")]
    public IActionResult Confirmation()
    {
        var sessionId = TempData.Peek(ConfirmationSessionIdKey) as string
                        ?? CpdSessionIdentity.Ensure(HttpContext.Session);
        var model = new SearchFeedbackViewModel { SessionId = sessionId };
        return View("~/Views/Search/FeedbackConfirmation.cshtml", model);
    }

    // Pulls the signed-in user's email from the ICurrentUserService claim reader. Returns
    // null when the service is not wired (bare unit construction) or when the claim was
    // empty (anonymous user). Whitespace-only claims are treated as absent so the input
    // renders empty rather than pre-filled with a blank string.
    private string? ResolveAutoFillEmail()
    {
        var email = _currentUser?.Email;
        return string.IsNullOrWhiteSpace(email) ? null : email;
    }

    // Formats the session's last search into a single line suitable for pre-filling the
    // "What did you actually get?" textarea. Uses invariant HH:mm + long-month formatting
    // to match GDS date conventions without an extra localisation dependency. The dedicated
    // "Your last search" panel above the textarea now shows the actual hits — this prefill
    // remains only as a starting point for the user's own words.
    private static string FormatPrefill(SearchEventForPrefill latest)
    {
        var query = latest.QueryRaw ?? latest.QueryNormalised ?? string.Empty;
        return string.Format(
            CultureInfo.InvariantCulture,
            "Search: \"{0}\" returned {1} results at {2:HH:mm} on {2:d MMM yyyy}",
            query,
            latest.ResultsTotal,
            latest.OccurredAtUtc);
    }

    // Projects the DTO into the view-model display record so the view has no dependency
    // on the Application-layer DTO shape (view files sit above ViewModels in the
    // dependency graph).
    private static PriorSearchDisplay ToDisplay(SearchEventForPrefill latest)
    {
        var query = latest.QueryRaw ?? latest.QueryNormalised ?? string.Empty;
        var hits = latest.Hits
            .Select(h => new PriorSearchHit(h.Position, h.ResultKind, h.ResultKey))
            .ToList();
        return new PriorSearchDisplay(query, latest.OccurredAtUtc, latest.ResultsTotal, hits);
    }
}
