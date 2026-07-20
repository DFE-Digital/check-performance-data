using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Web.Analytics;
using DfE.CheckPerformanceData.Web.Session;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

public sealed class WhatToChangeController(
    ICheckYourPupilDataService service,
    IQuestionFlowService flowService,
    IAnalyticsService analytics) : Controller
{
    [Route("/WhatToChange/{windowId}")]
    public IActionResult Index(Guid windowId)
    {
        var journey = HttpContext.Session.GetRequestState(windowId);
        return View(new WhatToChangeViewModel
        {
            WindowId = windowId,
            SelectedWhatToChange = journey.SelectedWhatToChange
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route("/WhatToChange/{windowId}")]
    public async Task<IActionResult> Confirm(Guid windowId, WhatToChangeViewModel vm)
    {
        if (vm.SelectedWhatToChange == null)
        {
            ModelState.AddModelError(nameof(WhatToChangeViewModel.SelectedWhatToChange), "Select what pupil data you would like to change");
            await analytics.TrackSafeAsync(new ValidationErrorEvent { ErrorCount = 1, ErrorCodes = [ValidationErrorCoding.NoSelection] });
            return View("Index", new WhatToChangeViewModel { WindowId = windowId, SelectedWhatToChange = null });
        }

        var window = await service.GetCheckingWindowAsync(windowId);

        HttpContext.Session.SaveRequestState(windowId, s =>
        {
            s.SelectedWhatToChange = vm.SelectedWhatToChange;
            s.CheckingWindow = window;
        });
        // A freshly started journey is never an edit of an existing request.
        HttpContext.Session.ClearBulkEditMode(windowId);
        HttpContext.Session.ClearSingleEditMode(windowId);

        await analytics.TrackSafeAsync(new ChangeTypeSelectedEvent
        {
            WhatToChange = vm.SelectedWhatToChange.Value.ToString(),
            CheckingWindowType = window.CheckingWindowType.ToString(),
        });

        var config = await flowService.GetConfigAsync(vm.SelectedWhatToChange.Value, window.CheckingWindowType);
        if (config is null) return RedirectToAction("Index", "CheckYourPupilData", new { windowId });

        return RedirectToAction("Page", "Journey", new { windowId, pageId = config.FirstPageId });
    }
}
