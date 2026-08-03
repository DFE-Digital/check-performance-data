using DfE.CheckPerformanceData.Application.Analytics;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels;

// Detail-view model for /admin/Messages/Inbox/{id}. Wraps the SearchMessageDetail
// projection with the derived link URL for the session drill-in and the pre-submission
// search snapshot the reviewer needs to reason about the user's note. All persisted
// fields are surfaced verbatim (Razor auto-encodes on render); the cross-link href is
// the only computed value.
public sealed class MessagesDetailViewModel
{
    public required SearchMessageDetail Message { get; init; }

    // The newest search the session ran at or before the message was submitted, if any.
    // Populated by the controller so the reviewer sees WHAT the user was looking at when
    // they filed the feedback — the same layout the user saw on the feedback form. Null
    // when the session either had never searched or every search happened after the note
    // was submitted; the view degrades to a hint instead of the panel.
    public PriorSearchDisplay? PriorSearch { get; init; }

    public string SessionDrillInHref => "/admin/Search/Session/" + Message.SessionId;
}
