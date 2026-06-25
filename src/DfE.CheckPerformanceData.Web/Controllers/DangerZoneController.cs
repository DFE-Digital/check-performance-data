using DfE.CheckPerformanceData.Web.Seeding;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;

namespace DfE.CheckPerformanceData.Web.Controllers;

// Danger zone: destructive operational actions, gated by the cypmd_admin role and a hard
// Production guard (the nav entries are not registered in prod, and every action 404s there
// too — defence in depth). The "Reset seed data" flow is a GET interstitial warning page
// followed by a POST that runs the full startup seeding sequence (wipes change requests +
// checking windows and reseeds the database, pupil data and question flows).
[Authorize(Roles = WikiConstants.AdminRole)]
[Route("admin/danger-zone")]
public sealed class DangerZoneController(
    IDevDataSeedingOrchestrator orchestrator,
    IHostEnvironment environment) : Controller
{
    [HttpGet("reset-seed-data")]
    public IActionResult ResetSeedData()
    {
        if (environment.IsProduction())
            return NotFound();

        return View();
    }

    [HttpPost("reset-seed-data")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetSeedDataConfirm()
    {
        if (environment.IsProduction())
            return NotFound();

        await orchestrator.RunAsync();

        TempData["DangerZoneResult"] = "Seed data has been reset. The database, pupil data and question flows are back to the default seeded state.";
        return Redirect("/admin");
    }
}
