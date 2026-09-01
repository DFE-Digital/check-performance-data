using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Analytics;
using DfE.CheckPerformanceData.Web.Common;
using DfE.CheckPerformanceData.Web.Controllers.Journey;
using DfE.CheckPerformanceData.Web.Session;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

public sealed class WhatToChangeController(
    ICheckYourPupilDataService service,
    IQuestionFlowService flowService,
    ICheckingExerciseService checkingExercises,
    IAnalyticsService analytics) : Controller
{
    // #318: every WhatToChange option belongs to pupil data checking, so both actions gate on that
    // one exercise. The option list on Check your pupil data is presentation only — a bookmarked
    // URL still reaches here after the exercise closes.
    private const CheckingExerciseType Exercise = CheckingExerciseType.PupilData;

    [Route("/WhatToChange/{windowId}")]
    public async Task<IActionResult> Index(Guid windowId)
    {
        var window = await service.GetCheckingWindowAsync(windowId);
        if (!checkingExercises.IsOpen(window.Exercises, Exercise))
            return this.RedirectExerciseClosed(windowId, Exercise, LearnerNoun.For(window.CheckingWindowType));

        var journey = HttpContext.Session.GetRequestState(windowId);
        return View(new WhatToChangeViewModel
        {
            WindowId = windowId,
            SelectedWhatToChange = journey.SelectedWhatToChange,
            CheckingWindowType = window.CheckingWindowType,
            LearnerNoun = LearnerNoun.For(window.CheckingWindowType)
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route("/WhatToChange/{windowId}")]
    public async Task<IActionResult> Confirm(Guid windowId, WhatToChangeViewModel vm)
    {
        var window = await service.GetCheckingWindowAsync(windowId);
        if (!checkingExercises.IsOpen(window.Exercises, Exercise))
            return this.RedirectExerciseClosed(windowId, Exercise, LearnerNoun.For(window.CheckingWindowType));

        if (vm.SelectedWhatToChange == null)
        {
            var noun = LearnerNoun.For(window.CheckingWindowType);
            ModelState.AddModelError(nameof(WhatToChangeViewModel.SelectedWhatToChange), $"Select what {noun.Singular} data you would like to change");
            await analytics.TrackSafeAsync(new ValidationErrorEvent { ErrorCount = 1, ErrorCodes = [ValidationErrorCoding.NoSelection], ErrorFields = [nameof(WhatToChangeViewModel.SelectedWhatToChange)] });
            return View("Index", new WhatToChangeViewModel { WindowId = windowId, SelectedWhatToChange = null, CheckingWindowType = window.CheckingWindowType, LearnerNoun = noun });
        }

        // AB#297310: the Add journey exists only for the window types that have an Add_*.json.
        // The radio is hidden elsewhere, but a posted form still arrives here, and a flow file
        // uploaded to blob for an unsupported window would otherwise open the journey with
        // nothing failing. Checked before the flow service or the session is touched.
        if (vm.SelectedWhatToChange == WhatToChange.Add
            && !AddPupilJourney.SupportedWindowTypes.Contains(window.CheckingWindowType))
            return RedirectToAction("Index", "CheckYourPupilData", new { windowId });

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
        //
        // A missing flow means "we don't know this journey", not "this journey has no pupil
        // search" — resetting on it would let any radio without a flow file, or a forged enum
        // value, destroy an in-progress journey that pre-dated the click. Unknown flow leaves
        // state alone; the redirect below already sends the user back.
        var preservesExistingState = config is null
            || config.Pages.Any(p => p.Type == PageType.PupilSearch);

        HttpContext.Session.SaveRequestState(windowId, s =>
        {
            s.SelectedWhatToChange = vm.SelectedWhatToChange;
            s.CheckingWindow = window;

            if (preservesExistingState) return;

            s.ReferenceNumber = null;
            s.SelectedPupil = null;
            s.SelectedPupilId = null;
            s.SelectedPupilLabel = null;
            s.MatchedPupil = null;
            s.MatchedPupilId = null;
            s.MatchedPupilLabel = null;
            s.SelectedResult = null;
            // AB#297848: the missing-qualification enquiry's equivalent of SelectedResult. Inert on
            // today's amendment journeys, which never read it — cleared anyway because this block's
            // whole argument is that it must not leave anything of the previous journey behind.
            s.SelectedQualification = null;
            // Captured by the EAL pages only, and OriginCountryLanguageCapture early-returns on a
            // page without country-originally-from — so nothing on a pupil-search-less flow would
            // ever clear an abandoned journey's country data before it reaches the new request.
            s.OriginCountryCode = null;
            s.OriginCountryLanguages = null;
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
