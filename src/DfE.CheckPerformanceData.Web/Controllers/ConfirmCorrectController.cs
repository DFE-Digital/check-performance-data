using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.CurrentUser;
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
    ICurrentUserService currentUserService,
    IAnalyticsService analytics) : Controller
{
    // #318: confirming the data is correct is a pupil-data-checking action, so it closes with that
    // exercise even while the outer window (and any other exercise on it) is still running.
    // AB#307773: it closes for the signed-in school, whose extension may run past the exercise's
    // own end, and the deadline the page and the email quote is that school's too:
    // the outer window's end is the exercises' own latest end and never includes an extension's
    // (extension ends never reach the stored window end), so it is only the fallback for a
    // window with no pupil-data row.
    private const CheckingExerciseType Exercise = CheckingExerciseType.PupilData;

    [HttpGet]
    public async Task<IActionResult> Index(Guid windowId)
    {
        var window = await service.GetCheckingWindowAsync(windowId);
        if (!checkingExercises.IsOpen(window.Exercises, Exercise, currentUserService.OrganisationLaestab))
            return this.RedirectExerciseClosed(windowId, Exercise, LearnerNoun.For(window.CheckingWindowType));

        var confirmVw = new ConfirmCorrectViewModel(windowId, DeadlineLabel(Deadline(window.Exercises, window.EndDate)));
        return View(confirmVw);
    }

    [HttpPost]
    public async Task<IActionResult> Confirm(Guid windowId)
    {
        var window = await service.GetCheckingWindowAsync(windowId);
        if (!checkingExercises.IsOpen(window.Exercises, Exercise, currentUserService.OrganisationLaestab))
            return this.RedirectExerciseClosed(windowId, Exercise, LearnerNoun.For(window.CheckingWindowType));

        var reference = journeyService.GenerateReference(window.CheckingWindowType);
        var deadline = Deadline(window.Exercises, window.EndDate);
        await requestService.ConfirmDataCorrectAsync(windowId, reference, deadline, EmailSubstitutions.From(window));

        await analytics.TrackSafeAsync(new CorrectDataConfirmedEvent
        {
            ReferenceNumber = reference,
            CheckingWindowType = window.CheckingWindowType.ToString(),
        });

        var confirmedVw = new ConfirmedCorrectViewModel(DeadlineLabel(deadline), reference);
        return View(confirmedVw);
    }

    private DateTime Deadline(IReadOnlyList<CheckingExerciseDto> exercises, DateTime outerEnd) =>
        checkingExercises.EndDateFor(exercises, Exercise, currentUserService.OrganisationLaestab) ?? outerEnd;

    // The time shows its minutes when the end date has some (DeadlineTime); the date and the
    // letter case are as this page has always printed them.
    private static string DeadlineLabel(DateTime endDate) =>
        endDate.ToString($"{DeadlineTime.Pattern(endDate)} 'on' dddd d MMMM");
}
