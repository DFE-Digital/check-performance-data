using System.ComponentModel.DataAnnotations;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Web.Session;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

public class PupilSearchController(ICheckYourPupilDataService service) : Controller
{
    [Route("/PupilSearch/{windowId}")]
    public IActionResult Index(Guid windowId)
    {
        var journey = HttpContext.Session.GetJourneyState(windowId);
        
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
        if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
            return Json(Array.Empty<object>());

        var journey = HttpContext.Session.GetJourneyState(windowId);
        var suggestions = await service.GetPupilSuggestionsAsync(windowId, query, journey.SelectedWhatToChange);
        return Json(suggestions.Select(s => new { id = s.Id, label = s.Label }));
    }

    [HttpPost]
    [Route("/PupilSearch/{windowId}")]
    public async Task<IActionResult> Index(Guid windowId, PupilSearchIndexViewModel model)
    {
        if (string.IsNullOrEmpty(model.SelectedPupilId))
        {
            var journey = HttpContext.Session.GetJourneyState(windowId);
            var vm = new PupilSearchIndexViewModel { WindowId = windowId, WhatToChange = journey.SelectedWhatToChange ?? default };
            ModelState.AddModelError(nameof(PupilSearchIndexViewModel.SelectedPupilId), vm.ErrorMessage);
            return View(vm);
        }

        var pupil = await service.GetPupilAsync(windowId, Guid.Parse(model.SelectedPupilId));
        
        HttpContext.Session.SaveJourneyState(windowId, s =>
        {
            s.SelectedPupilLabel = model.SelectedPupilLabel;
            s.SelectedPupilId = model.SelectedPupilId;
            s.SelectedPupil = pupil;
        });

        return RedirectToAction("Index", "RemovePupil", new { windowId });
    }
}

