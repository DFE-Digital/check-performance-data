using DfE.CheckPerformanceData.Application.Analytics;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.ViewComponents;

// Renders a single unread-messages count badge in the admin chrome beside the DLQ badge.
// The count is read once per page load; there is no client-side polling.
public sealed class SearchMessagesInboxBadgeViewComponent(ISearchMessageService messages) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        var count = 0;
        try
        {
            count = await messages.GetUnreadCountAsync(HttpContext.RequestAborted);
        }
        catch
        {
            // The badge must never break the admin chrome if the count is unavailable.
            count = 0;
        }

        return View(count);
    }
}
