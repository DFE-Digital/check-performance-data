using DfE.CheckPerformanceData.Application.Queue;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.ViewComponents;

// Renders a single dead-letter-queue count badge in the admin chrome. The count is
// read once per page load; there is no client-side polling.
public sealed class DlqBadgeViewComponent(IQueueAdminService queueAdminService) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        var count = 0;
        try
        {
            count = await queueAdminService.GetDlqCountAsync(HttpContext.RequestAborted);
        }
        catch
        {
            // The badge must never break the admin chrome if the count is unavailable.
            count = 0;
        }

        return View(count);
    }
}
