using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

[Route("/ConfirmCorrect/{windowId}")]
public sealed class ConfirmCorrectController(
    ICheckYourPupilDataService service,
    IJourneyValidationService journeyService,
    IRequestService requestService) : Controller
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
        var deadline = $"{window.EndDate.ToString("htt").ToLower()} on {window.EndDate:dddd d MMMM yyyy}";
        await requestService.ConfirmDataCorrectAsync(windowId, reference, deadline);
        var confirmedVw = new ConfirmedCorrectViewModel(window.EndDate.ToString("htt 'on' dddd d MMMM"), reference);
        return View(confirmedVw);
    }
}

public class ConfirmedCorrectViewModel(string endDate, string referenceNumber)
{
    public string EndDate { get; } = endDate;
    public string ReferenceNumber { get; } = referenceNumber;
}

public class ConfirmCorrectViewModel(Guid windowId, string endDate)
{
    public Guid WindowId { get; } = windowId;
    public string EndDate { get; } = endDate;
}