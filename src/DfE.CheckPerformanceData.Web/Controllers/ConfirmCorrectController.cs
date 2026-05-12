using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

public sealed class ConfirmCorrectController : Controller
{
    public IActionResult Index(Guid windowId)
    {
        return View();
    }
}