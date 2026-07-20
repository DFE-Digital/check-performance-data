using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

[Route("/ConfirmCorrect/{windowId}")]
public sealed class ConfirmCorrectController(
    ICheckYourPupilDataService service,
    IJourneyValidationService journeyService,
    IRequestService requestService,
    IAnalyticsService analytics) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(Guid windowId)
    {
        var window = await service.GetCheckingWindowAsync(windowId);
        var confirmVw = new ConfirmCorrectViewModel(windowId, window.EndDate.ToString("htt 'on' dddd d MMMM"));
        return View(confirmVw);
    }

    [HttpPost]
    public async Task<IActionResult> Confirm(Guid windowId)
    {
        var window = await service.GetCheckingWindowAsync(windowId);
        var reference = journeyService.GenerateReference(window.CheckingWindowType);
        await requestService.ConfirmDataCorrectAsync(windowId, reference, window.EndDate);

        await analytics.TrackSafeAsync(new CorrectDataConfirmedEvent
        {
            ReferenceNumber = reference,
            CheckingWindowType = window.CheckingWindowType.ToString(),
        });

        var confirmedVw = new ConfirmedCorrectViewModel(window.EndDate.ToString("htt 'on' dddd d MMMM"), reference);
        return View(confirmedVw);
    }
}
