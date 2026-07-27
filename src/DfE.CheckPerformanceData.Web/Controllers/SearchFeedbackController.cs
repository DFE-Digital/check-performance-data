using System.Globalization;
using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

// User-facing feedback surface. Users land here from the "Not the results you were
// expecting?" callout on /search when they can't find what they wanted; the form gives
// them a way to send a note keyed by their session id so support can look up the actual
// searches they ran.
//
// Two invariants carry this controller:
//   - context.Session.Id is the ONLY source of the persisted session id. Any form field
//     the client sends under the SessionIdDisplayOnly name is discarded. The readonly
//     input on the form exists only so the user can quote the id.
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

    public SearchFeedbackController(
        ISearchMessageService messages,
        ISearchAnalyticsQueryService query)
    {
        _messages = messages;
        _query = query;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        await HttpContext.Session.LoadAsync(ct);
        var sessionId = HttpContext.Session.Id;

        // The prior-search pre-fill is best-effort: if the query service returns null
        // (no matching row for the session), the textarea renders empty.
        var latest = await _query.GetLatestSearchForSessionAsync(sessionId, ct);
        var prefill = latest is null
            ? null
            : FormatPrefill(latest);

        var model = new SearchFeedbackViewModel
        {
            SessionId = sessionId,
            PriorSearchPrefill = prefill,
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
        var sessionId = HttpContext.Session.Id;

        if (!ModelState.IsValid)
        {
            // Construct a fresh view model with the server's session id — never trust
            // the posted view model's SessionId property to carry it into the redisplay.
            // The user's inputs round-trip so they don't retype on validation failure.
            var redisplay = new SearchFeedbackViewModel
            {
                SessionId = sessionId,
                PriorSearchPrefill = null,
                WhatLookingFor = form.WhatLookingFor,
                WhatGot = form.WhatGot,
                Email = form.Email,
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
                        ?? HttpContext.Session.Id;
        var model = new SearchFeedbackViewModel { SessionId = sessionId };
        return View("~/Views/Search/FeedbackConfirmation.cshtml", model);
    }

    // Formats the session's last search into a single line suitable for pre-filling the
    // "What did you actually get?" textarea. Uses invariant HH:mm + long-month formatting
    // to match GDS date conventions without an extra localisation dependency.
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
}
