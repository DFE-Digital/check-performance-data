using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Analytics;
using DfE.CheckPerformanceData.Web.Common;
using DfE.CheckPerformanceData.Web.Session;
using Microsoft.AspNetCore.Mvc;
using CheckingWindowDto = DfE.CheckPerformanceData.Application.LandingPage.CheckingWindowDto;

namespace DfE.CheckPerformanceData.Web.Controllers;

[ServiceNavSection(ServiceNavSection.Pupils)]
public sealed class WhatToChangeController(
    ICheckYourPupilDataService service,
    IChangeTypeSelectionService changeTypes,
    ICheckingExerciseService checkingExercises,
    ICurrentUserService currentUserService,
    IAnalyticsService analytics) : Controller
{
    private const CheckingExerciseType Exercise = CheckingExerciseType.PupilData;

    [HttpGet]
    [Route("/WhatToChange/{windowId}")]
    public async Task<IActionResult> Index(Guid windowId)
    {
        var window = await service.GetCheckingWindowAsync(windowId);
        if (!checkingExercises.IsOpen(window.Exercises, Exercise, currentUserService.OrganisationLaestab))
            return this.RedirectExerciseClosed(windowId, Exercise, LearnerNoun.For(window.CheckingWindowType));

        var choice = await changeTypes.GetChoiceAsync(window.CheckingWindowType);
        if (choice.OnlyChange is { } onlyChange)
            return await StartJourneyAsync(windowId, window, onlyChange);

        var journey = HttpContext.Session.GetRequestState(windowId);
        return View(new WhatToChangeViewModel
        {
            WindowId = windowId,
            SelectedWhatToChange = journey.SelectedWhatToChange,
            LearnerNoun = LearnerNoun.For(window.CheckingWindowType),
            AvailableChanges = choice.Available
        });
    }

    [HttpGet]
    [Route("/WhatToChange/{windowId}/back")]
    public async Task<IActionResult> Back(Guid windowId)
    {
        var window = await service.GetCheckingWindowAsync(windowId);
        var choice = await changeTypes.GetChoiceAsync(window.CheckingWindowType);

        return choice.OnlyChange is null
            ? RedirectToAction(nameof(Index), new { windowId })
            : RedirectToAction("Index", "CheckYourPupilData", new { windowId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route("/WhatToChange/{windowId}")]
    public async Task<IActionResult> Confirm(Guid windowId, WhatToChangeViewModel vm)
    {
        var window = await service.GetCheckingWindowAsync(windowId);
        if (!checkingExercises.IsOpen(window.Exercises, Exercise, currentUserService.OrganisationLaestab))
            return this.RedirectExerciseClosed(windowId, Exercise, LearnerNoun.For(window.CheckingWindowType));

        if (vm.SelectedWhatToChange == null)
        {
            var noun = LearnerNoun.For(window.CheckingWindowType);
            var choice = await changeTypes.GetChoiceAsync(window.CheckingWindowType);
            ModelState.AddModelError(nameof(WhatToChangeViewModel.SelectedWhatToChange), $"Select what {noun.Singular} data you would like to change");
            await analytics.TrackSafeAsync(new ValidationErrorEvent { ErrorCount = 1, ErrorCodes = [ValidationErrorCoding.NoSelection], ErrorFields = [nameof(WhatToChangeViewModel.SelectedWhatToChange)] });
            return View("Index", new WhatToChangeViewModel { WindowId = windowId, SelectedWhatToChange = null, LearnerNoun = noun, AvailableChanges = choice.Available });
        }

        return await StartJourneyAsync(windowId, window, vm.SelectedWhatToChange.Value);
    }

    private async Task<IActionResult> StartJourneyAsync(Guid windowId, CheckingWindowDto window, WhatToChange change)
    {
        var journey = HttpContext.Session.GetRequestState(windowId);
        var start = await changeTypes.StartJourneyAsync(journey, window, change);
        if (start.Recorded)
        {
            HttpContext.Session.SetRequestState(windowId, journey);
            HttpContext.Session.ClearBulkEditMode(windowId);
            HttpContext.Session.ClearSingleEditMode(windowId);
        }

        return start.FirstPageId is null
            ? RedirectToAction("Index", "CheckYourPupilData", new { windowId })
            : RedirectToAction("Page", "Journey", new { windowId, pageId = start.FirstPageId });
    }
}
