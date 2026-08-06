using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.ViewComponents;

// Renders a single consolidated count badge in the admin chrome that stands in for the
// two prior standalone DLQ + search-feedback-inbox badges. Reads both counts once per
// page load and surfaces their SUM as the tag number, linking to the Messages group
// landing so an admin can pick the specific inbox they want to inspect. Either read
// throwing degrades to zero for that side rather than breaking the chrome — the badge is
// a passive indicator, never a hard dependency of the layout.
//
// The two counts travel separately to the view: the total is one number, but the colour
// keys off the dead-letter count so a dead-lettering queue keeps the red alarm the
// standalone DLQ badge used to give it.
public sealed class MessagesBadgeViewComponent(
    ISearchMessageService messages,
    IQueueAdminService queueAdminService) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        var unread = 0;
        try
        {
            unread = await messages.GetUnreadCountAsync(HttpContext.RequestAborted);
        }
        catch
        {
            unread = 0;
        }

        var dlq = 0;
        try
        {
            dlq = await queueAdminService.GetDlqCountAsync(HttpContext.RequestAborted);
        }
        catch
        {
            dlq = 0;
        }

        // Kept separate rather than pre-summed: the badge shows the combined total but has
        // to colour itself off the dead-letter count alone.
        return View(new MessagesBadgeViewModel(unread, dlq));
    }
}
