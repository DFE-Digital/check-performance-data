using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.QuestionFlow;
using DfE.CheckPerformanceData.Web.Session;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

public sealed class PupilSearchController(ICheckYourPupilDataService service, IQuestionFlowService flowService) : Controller
{
    [Route("/PupilSearch/{windowId}")]
    public IActionResult Index(Guid windowId)
    {
        var journey = HttpContext.Session.GetRequestState(windowId);

        return View(new PupilSearchIndexViewModel
        {
            WhatToChange = journey.SelectedWhatToChange ?? default,
            WindowId = windowId,
            SelectedPupilId = journey.SelectedPupilId,
            SelectedPupilLabel = journey.SelectedPupilLabel
        });
    }

    [Route("/PupilSearch/{windowId}/suggestions")]
    public async Task<IActionResult> Suggestions(Guid windowId, string? query)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length < 2 || query.Length > 100)
            return Json(Array.Empty<object>());

        var journey = HttpContext.Session.GetRequestState(windowId);
        var suggestions = await service.GetPupilSuggestionsAsync(windowId, query, journey.SelectedWhatToChange);
        return Json(suggestions.Select(s => new { id = s.Id, label = s.Label }));
    }

    [HttpPost]
    [Route("/PupilSearch/{windowId}")]
    public async Task<IActionResult> Index(Guid windowId, PupilSearchIndexViewModel model)
    {
        if (string.IsNullOrEmpty(model.SelectedPupilId))
        {
            var journey = HttpContext.Session.GetRequestState(windowId);
            var vm = new PupilSearchIndexViewModel { WindowId = windowId, WhatToChange = journey.SelectedWhatToChange ?? default };
            ModelState.AddModelError(nameof(PupilSearchIndexViewModel.SelectedPupilId), vm.WhatToChangeMessage);
            return View(vm);
        }

        var pupil = await service.GetPupilAsync(windowId, Guid.Parse(model.SelectedPupilId));

        var existingState = HttpContext.Session.GetRequestState(windowId);
        var reference = GenerateReference(existingState.CheckingWindowType);

        HttpContext.Session.SaveRequestState(windowId, s =>
        {
            s.SelectedPupilLabel = model.SelectedPupilLabel;
            s.SelectedPupilId = model.SelectedPupilId;
            s.SelectedPupil = pupil;
            s.ReferenceNumber = reference;
            s.QuestionAnswers = new Dictionary<string, QuestionAnswer>();
            s.QuestionHistory = new List<string>();
        });

        var state = HttpContext.Session.GetRequestState(windowId);
        if (state.SelectedWhatToChange.HasValue && state.CheckingWindowType.HasValue)
        {
            var config = flowService.GetConfig(state.SelectedWhatToChange.Value, state.CheckingWindowType.Value);
            if (config is not null)
                return RedirectToAction("Page", "Journey", new { windowId, pageId = config.FirstPageId });
        }

        return NotFound();
    }

    private static string GenerateReference(CheckingWindowType? windowType)
    {
        var type = windowType?.ToString() ?? "Unknown";
        var uniqueId = Guid.NewGuid().ToString("N")[..7].ToUpper();
        return $"CYPMD_{type}_{uniqueId}";
    }
}
