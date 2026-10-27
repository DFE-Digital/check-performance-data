using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using DfE.CheckPerformanceData.Application.Audit;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Dashboard;
using DfE.CheckPerformanceData.Application.Impersonation;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Infrastructure.Ingress;
using DfE.CheckPerformanceData.Web.Impersonation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace DfE.CheckPerformanceData.Web.Controllers;

[Authorize]
[DfE.CheckPerformanceData.Web.Admin.RequireAdminSection("impersonation")]
public sealed class ImpersonationController(
    ImpersonationSessionService session,
    IImpersonationAuditWriter audit,
    ICurrentUserService original,
    EstablishmentDirectory establishments,
    IWindowRepository windows,
    IIngressSummaryStore summaries,
    ILogger<ImpersonationController> logger) : Controller
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
        await session.SetPendingSelectionAsync(selection, HttpContext.RequestAborted);
        return RedirectToAction(nameof(Choices));
    }

    [HttpGet("admin/impersonation/choices")]
    public async Task<IActionResult> Choices()
    {
        if (!ImpersonationAccessPolicy.CanAccess(User) || !session.IsInitialised || session.IsImpersonating) return Forbid();
        if (session.PendingSelection is not { } selection) return RedirectToAction(nameof(Index));
        return View(await BuildChoicesAsync(selection, HttpContext.RequestAborted));
    }

    [HttpPost("admin/impersonation/begin")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Begin(ImpersonationBeginViewModel model)
    {
        if (!ImpersonationAccessPolicy.CanAccess(User) || !session.IsInitialised || session.IsImpersonating) return Forbid();
        if (session.PendingSelection is not { } selection) return RedirectToAction(nameof(Index));

        if (model.WindowId.HasValue != model.ExerciseId.HasValue || !ModelState.IsValid)
            return View("Choices", await BuildChoicesAsync(selection, HttpContext.RequestAborted));

        if (model.WindowId is { } windowId && model.ExerciseId is { } exerciseId)
        {
            var choices = await BuildChoicesAsync(selection, HttpContext.RequestAborted);
            if (!choices.Exercises.Any(choice => choice.WindowId == windowId && choice.ExerciseId == exerciseId))
            {
                choices.Warning = "That exercise is no longer available for this establishment. Choose another destination.";
                return View("Choices", choices);
            }
        }

        await audit.RecordAsync(new(original.UserId, selection.Laestab, selection.Urn, "Start", DateTime.UtcNow), HttpContext.RequestAborted);
        await session.SetSelectionAsync(selection, HttpContext.RequestAborted);
        return model.WindowId is { } destinationWindow
            ? RedirectToAction("Index", "CheckYourPupilData", new { windowId = destinationWindow })
            : RedirectToAction("Index", "LandingPage");
    }

    private async Task<ImpersonationChoicesViewModel> BuildChoicesAsync(EstablishmentSelection selection,
        CancellationToken cancellationToken)
    {
        var establishment = establishments.Find(selection.Laestab, selection.Urn);
        var model = new ImpersonationChoicesViewModel
        {
            EstablishmentName = establishment?.Name ?? "Establishment",
            Selection = selection
        };

        var allWindows = await windows.GetAllWindowsAsync(cancellationToken);
        foreach (var window in allWindows)
        foreach (var exercise in window.Exercises)
        {
            IngressSummaryDocument? summary;
            try
            {
                summary = await summaries.GetAsync(window.Id, exercise.Id, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not read impersonation summary ({ErrorCode}) for window {WindowId}, exercise {ExerciseId}",
                    "impersonation_summary_read_failed", window.Id, exercise.Id);
                model.HasSummaryReadErrors = true;
                continue;
            }

            if (summary is null || !string.Equals(summary.Outcome, "succeeded", StringComparison.OrdinalIgnoreCase)) continue;

            var files = summary.Files.Select(file =>
            {
                var laestab = file.Laestabs.FirstOrDefault(group =>
                    LaestabNormaliser.Normalise(group.Laestab) == LaestabNormaliser.Normalise(selection.Laestab));
                return (File: file, Laestab: laestab);
            }).Where(pair => pair.Laestab is not null && pair.Laestab.Totals.RowsImported > 0).ToList();
            if (files.Count == 0) continue;

            var laestabRowsImported = files.Sum(pair => pair.Laestab!.Totals.RowsImported);
            var matchingUrnRows = files.Sum(pair => pair.Laestab!.Urns?
                .Where(urn => string.Equals(urn.Urn?.Trim(), selection.Urn, StringComparison.Ordinal))
                .Sum(urn => urn.RowsImported) ?? 0);
            var otherUrnRows = files.Sum(pair => pair.Laestab!.Urns?
                .Where(urn => !string.Equals(urn.Urn?.Trim(), selection.Urn, StringComparison.Ordinal))
                .Sum(urn => urn.RowsImported) ?? 0);
            var hasMissingUrnData = files.Any(pair => pair.Laestab!.Urns is null);
            var isUrnVerified = !hasMissingUrnData && otherUrnRows == 0 && matchingUrnRows == laestabRowsImported;
            var stats = files.Select(pair =>
            {
                var totals = isUrnVerified
                    ? pair.Laestab!.Urns!.Where(urn => string.Equals(urn.Urn?.Trim(), selection.Urn, StringComparison.Ordinal))
                        .Aggregate(new ImpersonationDatasetStats(), (sum, urn) => sum.Add(urn.RowsRead, urn.RowsImported))
                    : new ImpersonationDatasetStats(pair.Laestab!.Totals.RowsRead, pair.Laestab.Totals.RowsImported);
                return new ImpersonationDatasetStatViewModel(pair.File.DatasetName, totals.RowsRead, totals.RowsImported);
            }).ToList();

            var urnStatus = isUrnVerified ? "URN matched" : hasMissingUrnData
                ? "URN could not be verified" : "URN does not match";
            model.Exercises.Add(new ImpersonationExerciseChoiceViewModel
            {
                WindowId = window.Id,
                WindowTitle = window.Title,
                ExerciseId = exercise.Id,
                ExerciseName = string.IsNullOrWhiteSpace(exercise.Name) ? exercise.TabName : exercise.Name,
                UrnMatched = isUrnVerified,
                UrnStatus = urnStatus,
                StatsAreLaestabLevel = !isUrnVerified,
                Datasets = stats
            });
        }

        return model;
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

public sealed class ImpersonationBeginViewModel
{
    public Guid? WindowId { get; set; }
    public Guid? ExerciseId { get; set; }
}

public sealed class ImpersonationChoicesViewModel
{
    public required string EstablishmentName { get; init; }
    public required EstablishmentSelection Selection { get; init; }
    public List<ImpersonationExerciseChoiceViewModel> Exercises { get; } = [];
    public bool HasSummaryReadErrors { get; set; }
    public string? Warning { get; set; }
}

public sealed class ImpersonationExerciseChoiceViewModel
{
    public Guid WindowId { get; init; }
    public required string WindowTitle { get; init; }
    public Guid ExerciseId { get; init; }
    public required string ExerciseName { get; init; }
    public bool UrnMatched { get; init; }
    public required string UrnStatus { get; init; }
    public bool StatsAreLaestabLevel { get; init; }
    public required IReadOnlyList<ImpersonationDatasetStatViewModel> Datasets { get; init; }
}

public sealed record ImpersonationDatasetStatViewModel(string DatasetName, int RowsRead, int RowsImported);

internal sealed record ImpersonationDatasetStats(int RowsRead = 0, int RowsImported = 0)
{
    public ImpersonationDatasetStats Add(int rowsRead, int rowsImported) =>
        new(RowsRead + rowsRead, RowsImported + rowsImported);
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
