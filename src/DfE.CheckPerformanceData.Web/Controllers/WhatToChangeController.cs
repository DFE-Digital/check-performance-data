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
    public async Task<IActionResult> Index(Guid windowId)
    {
        var journey = HttpContext.Session.GetRequestState(windowId);
        var window = await service.GetCheckingWindowAsync(windowId);
        return View(new WhatToChangeViewModel
        {
            WindowId = windowId,
            SelectedWhatToChange = journey.SelectedWhatToChange,
            CheckingWindowType = window.CheckingWindowType
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route("/WhatToChange/{windowId}")]
    public async Task<IActionResult> Confirm(Guid windowId, WhatToChangeViewModel vm)
    {
        var window = await service.GetCheckingWindowAsync(windowId);

        if (vm.SelectedWhatToChange == null)
        {
            ModelState.AddModelError(nameof(WhatToChangeViewModel.SelectedWhatToChange), "Select what pupil data you would like to change");
            await analytics.TrackSafeAsync(new ValidationErrorEvent { ErrorCount = 1, ErrorCodes = [ValidationErrorCoding.NoSelection], ErrorFields = [nameof(WhatToChangeViewModel.SelectedWhatToChange)] });
            return View("Index", new WhatToChangeViewModel { WindowId = windowId, SelectedWhatToChange = null, CheckingWindowType = window.CheckingWindowType });
        }

        HttpContext.Session.SaveRequestState(windowId, s =>
        {
            s.SelectedWhatToChange = vm.SelectedWhatToChange;
            s.CheckingWindow = window;

            // AB#297310: the Add journey has no pupil-search step to naturally refresh
            // SelectedPupil/ReferenceNumber the way every other flow's PupilSearchPost does (it
            // unconditionally regenerates both on every pupil selection). AddPupilJourney.BuildPupil
            // instead reuses them for stability across re-edits WITHIN one journey — so without a
            // reset here, restarting Add in the same browser session after a previous Add request
            // was already submitted would silently reuse its reference and pupil id, colliding with
            // (and overwriting) that submitted row. "A freshly started journey is never an edit of
            // an existing request" (see below) has to be made true for Add explicitly.
            if (vm.SelectedWhatToChange == WhatToChange.Add)
            {
                s.ReferenceNumber = null;
                s.SelectedPupil = null;
                s.SelectedPupilId = null;
                s.SelectedPupilLabel = null;
                s.QuestionAnswers = new();
                s.QuestionHistory = new();
            }
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
