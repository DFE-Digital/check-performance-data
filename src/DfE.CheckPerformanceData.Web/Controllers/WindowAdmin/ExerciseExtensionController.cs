using System.Globalization;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Application.WindowManagement.Extensions;
using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;

/// <summary>
/// AB#307773: adds and edits an extension of a checking exercise — a later end date for the schools
/// it names — from the window details page.
/// </summary>
/// <remarks>
/// Thin by design. Whether the save may happen is <see cref="IExerciseExtensionService"/>'s rule;
/// this parses the pasted list, asks for the warnings, and turns the service's answer into a field
/// error or a banner. Open and closed are asked of <see cref="ICheckingExerciseService"/>, the one
/// clock.
///
/// Add is allowed at any time, before or after the exercise ends. Edit only while the extension is
/// open: once closed its schools' time has run out and its requests may have been sent, so it is
/// read only. Both are re-checked on the POST: the form is not what authorises the save, the route
/// is.
///
/// The warning about schools with no data file is a confirm-on-resubmit step, as AddExerciseData's
/// journey-file warning is: the form comes back with the schools listed and a checkbox, and the next
/// POST carries the tick. No JavaScript is involved.
///
/// A data share has no kind and so no journey; there is no time to extend, and its id is not found
/// here.
/// </remarks>
[RequireAdminSection(AdminNavKeys.ManageWindow)]
public sealed class ExerciseExtensionController(
    IWindowService windowService,
    IExerciseExtensionService extensions,
    ICheckingExerciseService checkingExercises,
    ICurrentUserService currentUser) : Controller
{
    /// <summary>Carries the outcome sentence to the success banner on the summary page.</summary>
    public const string TempDataKey = "ExtensionOutcome";

    public const string NoSchoolsError = "Enter at least one school";
    public const string EndDateNotInFutureError = "The extension end date must be in the future";
    public const string ClosedRefusal = "This extension has closed and cannot be changed.";
    public const string StaleRefusal = "This extension was changed by someone else. Check it and try again.";

    private const string PageView = "~/Views/WindowAdmin/ExerciseExtension.cshtml";

    [HttpGet("admin/windows/{id:guid}/exercises/{exerciseId:guid}/extensions/new")]
    public async Task<IActionResult> New(Guid id, Guid exerciseId, CancellationToken cancellationToken)
    {
        var window = await windowService.GetByIdAsync(id, cancellationToken);
        if (FindExtendable(window, exerciseId) is not { } row)
            return NotFound();

        var model = new ExerciseExtensionItem();
        Decorate(model, id, row, extensionId: null);
        return View(PageView, model);
    }

    [HttpPost("admin/windows/{id:guid}/exercises/{exerciseId:guid}/extensions/new")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        Guid id, Guid exerciseId, ExerciseExtensionItem model, CancellationToken cancellationToken)
    {
        var window = await windowService.GetByIdAsync(id, cancellationToken);
        if (FindExtendable(window, exerciseId) is not { } row)
            return NotFound();

        Decorate(model, id, row, extensionId: null);

        if (Validate(model) is not { } laestabs || await NeedsConfirmingAsync(model, laestabs, cancellationToken))
            return View(PageView, model);

        var result = await extensions.AddAsync(Command(model, laestabs), cancellationToken);
        return Outcome(model, result, laestabs, "added");
    }

    [HttpGet("admin/windows/{id:guid}/exercises/{exerciseId:guid}/extensions/{extensionId:guid}/edit")]
    public async Task<IActionResult> Edit(
        Guid id, Guid exerciseId, Guid extensionId, CancellationToken cancellationToken)
    {
        var window = await windowService.GetByIdAsync(id, cancellationToken);
        if (FindExtension(window, exerciseId, extensionId) is not (var row, var extension))
            return NotFound();

        // The summary offers Edit only while the extension is open, but a bookmarked URL or a tab
        // left open across the end date still arrives here.
        if (checkingExercises.HasClosed(extension))
            return Refuse(id, ClosedRefusal);

        var model = new ExerciseExtensionItem
        {
            PreviousEnd = ExerciseExtensionItem.GuardFor(extension.EndDate),
            EndDate = extension.EndDate.Date,
            EndHour = extension.EndDate.Hour,
            EndMinute = extension.EndDate.Minute,
            Schools = LaestabList.DisplayLines(extension.Laestabs)
        };
        Decorate(model, id, row, extensionId);
        return View(PageView, model);
    }

    [HttpPost("admin/windows/{id:guid}/exercises/{exerciseId:guid}/extensions/{extensionId:guid}/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(
        Guid id, Guid exerciseId, Guid extensionId, ExerciseExtensionItem model, CancellationToken cancellationToken)
    {
        var window = await windowService.GetByIdAsync(id, cancellationToken);
        if (FindExtension(window, exerciseId, extensionId) is not (var row, var extension))
            return NotFound();

        if (checkingExercises.HasClosed(extension))
            return Refuse(id, ClosedRefusal);

        // The page always posts the end date it showed. Without it the save has nothing to compare
        // against, and would overwrite whatever another admin did meanwhile.
        if (!model.TryReadPreviousEnd(out var previousEnd))
            return BadRequest();

        Decorate(model, id, row, extensionId);

        if (Validate(model) is not { } laestabs || await NeedsConfirmingAsync(model, laestabs, cancellationToken))
            return View(PageView, model);

        var result = await extensions.EditAsync(extensionId, previousEnd, Command(model, laestabs), cancellationToken);
        return Outcome(model, result, laestabs, "updated");
    }

    /// <summary>The exercise, when the window holds it and it has a kind: see the class remarks.</summary>
    internal static CheckingExerciseDto? FindExtendable(CheckingWindowDto? window, Guid exerciseId) =>
        window?.Exercises.SingleOrDefault(e => e.Id == exerciseId && e.ExerciseType is not null);

    /// <summary>
    /// The exercise and its extension, or null for an unknown window, exercise or extension and for
    /// a data share. Shared with <see cref="CloseExtensionController"/> and
    /// <see cref="SendExtensionRequestsController"/>, so the three pages find the same rows.
    /// </summary>
    internal static (CheckingExerciseDto Row, CheckingExerciseExtensionDto Extension)? FindExtension(
        CheckingWindowDto? window, Guid exerciseId, Guid extensionId) =>
        FindExtendable(window, exerciseId) is { } row
        && row.Extensions.SingleOrDefault(x => x.Id == extensionId) is { } extension
            ? (row, extension)
            : null;

    /// <summary>"08/10/2026 17:00": how every extension date is quoted, on the same clock as the exercise's.</summary>
    internal static string Format(DateTime value) => value.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

    internal static string Pluralise(int count, string noun) =>
        count == 1 ? $"1 {noun}" : $"{count} {noun}s";

    // The ids come from the route, never the form; the label and the exercise end are not posted,
    // so a redisplayed page has to be given them again.
    private static void Decorate(ExerciseExtensionItem model, Guid windowId, CheckingExerciseDto row, Guid? extensionId)
    {
        model.WindowId = windowId;
        model.ExerciseId = row.Id;
        model.ExtensionId = extensionId;
        model.ExerciseLabel = CloseExerciseController.NameOf(row);
        model.ExerciseEnd = row.EndDate;
    }

    // The form's own checks, after the binder's date checks: every bad line is reported at once, in
    // the order the admin sees them. Null when anything is wrong, else the schools as digits.
    private IReadOnlyList<string>? Validate(ExerciseExtensionItem model)
    {
        var parsed = LaestabList.Parse(model.Schools);
        foreach (var error in parsed.Errors)
            ModelState.AddModelError(nameof(ExerciseExtensionItem.Schools), error.Message);

        if (parsed.IsValid && parsed.Laestabs.Count == 0)
            ModelState.AddModelError(nameof(ExerciseExtensionItem.Schools), NoSchoolsError);

        return ModelState.ErrorCount == 0 ? parsed.Laestabs : null;
    }

    // A warning, not an error: the admin may extend a school whose data arrives in a later release.
    // Asked until the admin ticks the box; the tick is only on the page while the warning is.
    private async Task<bool> NeedsConfirmingAsync(
        ExerciseExtensionItem model, IReadOnlyList<string> laestabs, CancellationToken cancellationToken)
    {
        if (model.ConfirmNoData)
            return false;

        var warnings = await extensions.FindSchoolsWithoutDataAsync(
            model.WindowId, model.ExerciseId, laestabs, cancellationToken);
        if (warnings.LaestabsWithoutData.Count == 0 && !warnings.ExerciseHasNoRelease)
            return false;

        model.SchoolsWithoutData = warnings.LaestabsWithoutData.Select(LaestabList.Display).ToList();
        model.ExerciseHasNoRelease = warnings.ExerciseHasNoRelease;

        // An error on the checkbox, not only the warning: without it the page comes back looking
        // as it was sent, with nothing announced, and a screen-reader user would take the save to
        // have gone through. As AddExerciseData does for its journey-file warning.
        ModelState.AddModelError(nameof(ExerciseExtensionItem.ConfirmNoData), model.ConfirmNoDataError);
        return true;
    }

    private ExtensionCommand Command(ExerciseExtensionItem model, IReadOnlyList<string> laestabs) =>
        new(model.WindowId, model.ExerciseId, model.EndDateTime!.Value, laestabs,
            new ExtensionActor(currentUser.UserId, currentUser.DisplayName));

    private IActionResult Outcome(
        ExerciseExtensionItem model, ExtensionSaveResult result, IReadOnlyList<string> laestabs, string verb)
    {
        switch (result.Status)
        {
            case ExtensionSaveStatus.Saved:
                TempData[TempDataKey] =
                    $"Extension {verb} for {Pluralise(laestabs.Count, "school")}, ending {Format(model.EndDateTime!.Value)}.";
                return Redirect(model.CancelLink);

            case ExtensionSaveStatus.EndDateNotAfterExercise:
                ModelState.AddModelError(nameof(ExerciseExtensionItem.EndDate),
                    $"The extension must end after the exercise ends on {Format(result.ExerciseEnd ?? model.ExerciseEnd)}");
                return View(PageView, model);

            case ExtensionSaveStatus.EndDateNotInFuture:
                ModelState.AddModelError(nameof(ExerciseExtensionItem.EndDate), EndDateNotInFutureError);
                return View(PageView, model);

            case ExtensionSaveStatus.NoSchools:
                ModelState.AddModelError(nameof(ExerciseExtensionItem.Schools), NoSchoolsError);
                return View(PageView, model);

            case ExtensionSaveStatus.SchoolInAnotherExtension:
                foreach (var conflict in result.Conflicts ?? [])
                {
                    ModelState.AddModelError(nameof(ExerciseExtensionItem.Schools),
                        $"{LaestabList.Display(conflict.Laestab)} is already in the extension ending {Format(conflict.OtherExtensionEnd)}.");
                }
                return View(PageView, model);

            // Not errors on the form: nothing the admin typed was wrong, the extension moved under
            // them. They go back to the summary, which shows it as it now stands.
            case ExtensionSaveStatus.Closed:
                return Refuse(model.WindowId, ClosedRefusal);

            case ExtensionSaveStatus.Stale:
                return Refuse(model.WindowId, StaleRefusal);

            default:
                return NotFound();
        }
    }

    private RedirectResult Refuse(Guid windowId, string sentence)
    {
        TempData[SendExerciseRequestsController.RefusedTempDataKey] = sentence;
        return Redirect($"/admin/windows/summary/{windowId}");
    }
}
