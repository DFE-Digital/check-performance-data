using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

[AllowAnonymous]
public sealed class PrivacyController : Controller
{
    [HttpGet("/privacy")]
    public IActionResult Index() => View();
}
