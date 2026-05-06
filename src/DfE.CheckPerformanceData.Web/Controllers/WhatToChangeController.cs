using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Web.Session;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

public class WhatToChangeController : Controller
{
    [Route("/WhatToChange/{windowId}")]
    public IActionResult Index(Guid windowId)
    {
        var journey = HttpContext.Session.GetJourneyState(windowId);
        return View(new WhatToChangeViewModel
        {
            WindowId = windowId,
            SelectedWhatToChange = journey.SelectedWhatToChange
        });
    }

    [Route("/WhatToChange/{windowId}")]
    [HttpPost]
    public IActionResult Confirm(Guid windowId, WhatToChangeViewModel vm)
    {
        if (vm.SelectedWhatToChange == null)
        {
            ModelState.AddModelError(nameof(WhatToChangeViewModel.SelectedWhatToChange), "Select what pupil data you would like to change");
            return View("Index", new WhatToChangeViewModel { WindowId = windowId, SelectedWhatToChange = null });
        }

        HttpContext.Session.SaveJourneyState(windowId, s => s.SelectedWhatToChange = vm.SelectedWhatToChange);

        return RedirectToAction("Index", "PupilSearch", new { windowId });
    }
}

public class WhatToChangeViewModel
{
    public Guid WindowId { get; set; }
    public WhatToChange? SelectedWhatToChange { get; set; }
}

