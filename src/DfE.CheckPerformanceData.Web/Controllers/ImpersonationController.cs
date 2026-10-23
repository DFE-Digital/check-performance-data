using System.ComponentModel.DataAnnotations;
using DfE.CheckPerformanceData.Application.Audit;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Impersonation;
using DfE.CheckPerformanceData.Web.Impersonation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

[Authorize]
[DfE.CheckPerformanceData.Web.Admin.RequireAdminSection("impersonation")]
public sealed class ImpersonationController(ImpersonationSessionService session, IImpersonationAuditWriter audit, ICurrentUserService original) : Controller
{
    [HttpGet("admin/impersonation")]
    public IActionResult Index() => ImpersonationAccessPolicy.CanAccess(User) && session.IsInitialised
        ? View(new ImpersonationViewModel()) : Forbid();

    [HttpPost("admin/impersonation")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Start(ImpersonationViewModel model)
    {
        if (!ImpersonationAccessPolicy.CanAccess(User) || !session.IsInitialised || session.IsImpersonating) return Forbid();
        if (!ModelState.IsValid) return View("Index", model);
        var selection = new EstablishmentSelection(model.Laestab ?? "", model.Urn ?? "", model.LowestAge!.Value, model.HighestAge!.Value);
        await audit.RecordAsync(new(original.UserId, selection.Laestab, selection.Urn, "Start", DateTime.UtcNow), HttpContext.RequestAborted);
        await session.SetSelectionAsync(selection, HttpContext.RequestAborted);
        return RedirectToAction("Index", "LandingPage");
    }

    [HttpPost("impersonation/exit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Exit()
    {
        if (!session.IsInitialised) return Forbid();
        if (session.IsImpersonating)
            await audit.RecordAsync(new(original.UserId, session.OrganisationLaestab, session.OrganisationUrn, "Exit", DateTime.UtcNow), HttpContext.RequestAborted);
        await session.SetSelectionAsync(null, HttpContext.RequestAborted);
        return RedirectToAction("Index", "Admin");
    }
}

public sealed class ImpersonationViewModel
{
    public string? Laestab { get; set; }
    public string? Urn { get; set; }
    [Required(ErrorMessage = "Enter the lowest age")]
    public int? LowestAge { get; set; }
    [Required(ErrorMessage = "Enter the highest age")]
    public int? HighestAge { get; set; }
}
