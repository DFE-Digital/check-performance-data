using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

[AllowAnonymous]
public sealed class AccessibilityController : Controller
{
    [HttpGet("/accessibility")]
    public IActionResult Index() => View();
}
