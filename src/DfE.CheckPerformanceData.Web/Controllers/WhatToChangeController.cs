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

        var config = await flowService.GetConfigAsync(vm.SelectedWhatToChange.Value, window.CheckingWindowType);

        // AB#297310: a flow that opens with a pupil search refreshes the whole per-request identity
        // itself — PupilSearchPost regenerates the reference and the selected pupil, and nulls the
        // matched pupil and the selected result, on every pupil selection. A flow WITHOUT one (the
        // Add journey is the first) has nothing that does any of that, so whatever the previous
        // journey left in session survives into the new one: an already-submitted request's
        // reference would be reused and its row overwritten by the upsert, and an abandoned Merge
        // journey's matched pupil would surface on the Add summary as "Second record to merge".
        // Keyed on the flow's shape rather than on WhatToChange.Add so the next pupil-search-less
        // journey inherits the guarantee instead of the bug.
        var refreshedByPupilSearch = config is not null
            && config.Pages.Any(p => p.Type == PageType.PupilSearch);

        HttpContext.Session.SaveRequestState(windowId, s =>
        {
            s.SelectedWhatToChange = vm.SelectedWhatToChange;
            s.CheckingWindow = window;

            if (refreshedByPupilSearch) return;

            s.ReferenceNumber = null;
            s.SelectedPupil = null;
            s.SelectedPupilId = null;
            s.SelectedPupilLabel = null;
            s.MatchedPupil = null;
            s.MatchedPupilId = null;
            s.MatchedPupilLabel = null;
            s.SelectedResult = null;
            s.QuestionAnswers = new();
            s.QuestionHistory = new();
        });
        // A freshly started journey is never an edit of an existing request.
        HttpContext.Session.ClearBulkEditMode(windowId);
        HttpContext.Session.ClearSingleEditMode(windowId);

        await analytics.TrackSafeAsync(new ChangeTypeSelectedEvent
        {
            WhatToChange = vm.SelectedWhatToChange.Value.ToString(),
            CheckingWindowType = window.CheckingWindowType.ToString(),
        });

        if (config is null) return RedirectToAction("Index", "CheckYourPupilData", new { windowId });

        return RedirectToAction("Page", "Journey", new { windowId, pageId = config.FirstPageId });
    }
}
