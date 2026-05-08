using System.Text.Json;
using DfE.CheckPerformanceData.Web.Session;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

public sealed class RemovePupilController : Controller
{
    [Route("/RemovePupil/{windowId}")]
    public IActionResult Index(Guid windowId)
    {
        var journey = HttpContext.Session.GetJourneyState(windowId);

        return View("Index", JsonSerializer.Serialize(journey));
    } 
}
