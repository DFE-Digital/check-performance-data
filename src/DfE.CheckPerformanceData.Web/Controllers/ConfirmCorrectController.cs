using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.Notify;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Common;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

[Route("/ConfirmCorrect/{windowId}")]
public sealed class ConfirmCorrectController(
    ICheckYourPupilDataService service,
    IJourneyValidationService journeyService,
    IRequestService requestService,
    ICheckingExerciseService checkingExercises,
    IAnalyticsService analytics) : Controller
{
    // #318: confirming the data is correct is a pupil-data-checking action, so it closes with that
    // exercise even while the outer window (and any other exercise on it) is still running.
    private const CheckingExerciseType Exercise = CheckingExerciseType.PupilData;

    [HttpGet]
    public async Task<IActionResult> Index(Guid windowId)
    {
        var window = await service.GetCheckingWindowAsync(windowId);
        if (!checkingExercises.IsOpen(window.Exercises, Exercise))
            return this.RedirectExerciseClosed(windowId, Exercise, LearnerNoun.For(window.CheckingWindowType));

        var confirmVw = new ConfirmCorrectViewModel(windowId, window.EndDate.ToString("htt 'on' dddd d MMMM"));
        return View(confirmVw);
    }

    [HttpPost]
    public async Task<IActionResult> Confirm(Guid windowId)
    {
        var window = await service.GetCheckingWindowAsync(windowId);
        if (!checkingExercises.IsOpen(window.Exercises, Exercise))
            return this.RedirectExerciseClosed(windowId, Exercise, LearnerNoun.For(window.CheckingWindowType));

        var reference = journeyService.GenerateReference(window.CheckingWindowType);
        await requestService.ConfirmDataCorrectAsync(windowId, reference, window.EndDate, EmailSubstitutions.From(window));

        await analytics.TrackSafeAsync(new CorrectDataConfirmedEvent
        {
            ReferenceNumber = reference,
            CheckingWindowType = window.CheckingWindowType.ToString(),
        });

        var confirmedVw = new ConfirmedCorrectViewModel(window.EndDate.ToString("htt 'on' dddd d MMMM"), reference);
        return View(confirmedVw);
    }
}
