using DfE.CheckPerformanceData.Web.Models.Guidance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

[AllowAnonymous]
public sealed class GuidanceController : Controller
{
    [HttpGet("/guidance")]
    public IActionResult Index() => View();

    [HttpGet("/guidance/2026-ks4-june-checking-exercise")]
    public IActionResult Ks4June2026() => View(GuidancePage.Ks4June2026);
}
