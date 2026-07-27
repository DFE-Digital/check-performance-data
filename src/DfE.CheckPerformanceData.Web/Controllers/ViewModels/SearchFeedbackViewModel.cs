using System.ComponentModel.DataAnnotations;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels;

// View-model for the user-facing feedback form at /Search/Feedback.
//
// The session-ID display value is INTENTIONALLY not bound to the POST payload — the
// controller re-reads context.Session.Id after model binding and ignores any value the
// client tried to inject under the SessionIdDisplayOnly name. The field lives on the
// form only so the user can see (and quote) the identifier the sink stamps onto their
// row; it is never trusted as input.
//
// HideMyEmail drops the email value BEFORE persist — no encryption, no reveal audit,
// no IDataProtectionProvider machinery. When the checkbox is ticked the persisted
// search_messages.email is literally NULL.
public sealed class SearchFeedbackViewModel
{
    // Server-populated display only. Rendered as a readonly govuk-input so the user can
    // see the identifier. The controller re-reads HttpContext.Session.Id on POST; the
    // property here is NOT [BindProperty] and any POST value is discarded.
    public string SessionId { get; set; } = string.Empty;

    // Optional pre-fill for the "What did you actually get?" textarea, drawn from the
    // session's most recent search event. Null when the session has no prior search.
    public string? PriorSearchPrefill { get; set; }

    [Required(ErrorMessage = "Enter what you were looking for")]
    [StringLength(4000, ErrorMessage = "Keep your message under 4000 characters")]
    public string? WhatLookingFor { get; set; }

    [StringLength(4000, ErrorMessage = "Keep your message under 4000 characters")]
    public string? WhatGot { get; set; }

    [EmailAddress(ErrorMessage = "Enter an email address in the correct format, like name@example.com")]
    public string? Email { get; set; }

    public bool HideMyEmail { get; set; }
}
