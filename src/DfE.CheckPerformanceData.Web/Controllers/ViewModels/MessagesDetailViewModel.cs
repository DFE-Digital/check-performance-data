using DfE.CheckPerformanceData.Application.Analytics;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels;

// Detail-view model for /admin/Messages/Inbox/{id}. Wraps the SearchMessageDetail
// projection with the derived link URL for the session drill-in so the view doesn't
// have to compose it inline. All persisted fields are surfaced verbatim (Razor auto-
// encodes on render); the cross-link href is the only computed value.
public sealed class MessagesDetailViewModel
{
    public required SearchMessageDetail Message { get; init; }

    public string SessionDrillInHref => "/admin/Search/Session/" + Message.SessionId;
}
