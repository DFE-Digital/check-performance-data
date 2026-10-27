using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using DfE.CheckPerformanceData.Application.Audit;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Impersonation;
using DfE.CheckPerformanceData.Web.Impersonation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

[Authorize]
[DfE.CheckPerformanceData.Web.Admin.RequireAdminSection("impersonation")]
public sealed class ImpersonationController(
    ImpersonationSessionService session,
    IImpersonationAuditWriter audit,
    ICurrentUserService original,
    EstablishmentDirectory establishments) : Controller
{
    [HttpGet("admin/impersonation")]
    public IActionResult Index() => ImpersonationAccessPolicy.CanAccess(User) && session.IsInitialised
        ? View(new ImpersonationViewModel()) : Forbid();

    [HttpGet("admin/impersonation/search")]
    public IActionResult Search([FromQuery] string? query)
    {
        if (!ImpersonationAccessPolicy.CanAccess(User) || !session.IsInitialised) return Forbid();
        return Json(establishments.Search(query));
    }

    [HttpPost("admin/impersonation")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Start(ImpersonationViewModel model)
    {
        if (!ImpersonationAccessPolicy.CanAccess(User) || !session.IsInitialised || session.IsImpersonating) return Forbid();
        if (!ModelState.IsValid) return View("Index", model);
        var selection = new EstablishmentSelection(model.Laestab!, model.Urn!, model.LowestAge!.Value, model.HighestAge!.Value);
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

public sealed class ImpersonationViewModel : IValidatableObject
{
    private static readonly Regex LaestabFormat = new("^[0-9]{7}$", RegexOptions.CultureInvariant);
    private static readonly Regex UrnFormat = new("^[0-9]{6}$", RegexOptions.CultureInvariant);

    [Required(ErrorMessage = "Enter a Laestab")]
    public string? Laestab { get; set; }
    [Required(ErrorMessage = "Enter an Urn")]
    public string? Urn { get; set; }
    [Required(ErrorMessage = "Enter the lowest age")]
    [Range(0, 150, ErrorMessage = "Enter an age between 0 and 150")]
    public int? LowestAge { get; set; }
    [Required(ErrorMessage = "Enter the highest age")]
    [Range(0, 150, ErrorMessage = "Enter an age between 0 and 150")]
    public int? HighestAge { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Laestab is not null && !LaestabFormat.IsMatch(Laestab))
            yield return new ValidationResult("Enter a valid LAESTAB", [nameof(Laestab)]);
        if (Urn is not null && !UrnFormat.IsMatch(Urn))
            yield return new ValidationResult("Enter a valid URN", [nameof(Urn)]);
        if (LowestAge.HasValue && HighestAge.HasValue && HighestAge.Value <= LowestAge.Value)
            yield return new ValidationResult("The highest age must be greater than the lowest age", [nameof(HighestAge)]);
    }
}
