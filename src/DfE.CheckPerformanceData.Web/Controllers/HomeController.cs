using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DfE.CheckPerformanceData.Web.Models;

namespace DfE.CheckPerformanceData.Web.Controllers;

[AllowAnonymous]
public sealed class HomeController() : Controller
{
    public async Task<IActionResult> Index()
    {
        return View();
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }

    // Targeted by UseStatusCodePagesWithReExecute so unmapped routes render text/html
    // via the full MVC pipeline (including the shared layout + injected session
    // comment). Without this the default 404 is text/plain and users cannot quote a
    // session id from a page they never really saw.
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public new IActionResult NotFound()
    {
        Response.StatusCode = 404;
        return View();
    }
}
