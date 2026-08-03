using DfE.CheckPerformanceData.Application.Analytics;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels;

// Drill-in view model for /admin/Search/Session/{id}. Carries the session id (rendered
// in the heading + used to build the delete-form action), a shortened display form for
// the modal body copy, the session's search-event history newest first, and the list of
// support messages the same session filed so the admin can navigate back into the
// message flow without leaving the drill-in.
public sealed class SearchSessionDrillInViewModel
{
    public required string SessionId { get; init; }
    public required IReadOnlyList<SessionHistoryRow> Events { get; init; }
    public required IReadOnlyList<SearchMessageSummary> Messages { get; init; }

    // First 8 chars followed by an ellipsis when the id is longer than 8, or the id
    // verbatim when it is not. Used in headings and modal copy so admins can eyeball a
    // session id without the URL cluttering the layout — the full value stays available
    // on the SessionId property.
    public string ShortSessionId => SessionId.Length <= 8
        ? SessionId
        : SessionId.Substring(0, 8) + "…";
}
