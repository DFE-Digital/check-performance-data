using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;

[RequireAdminSection(AdminNavKeys.ManageWindow)]
public sealed class EditCheckingExerciseController(IWindowService windowService) : Controller
{
    private const string PageView = "~/Views/WindowAdmin/CreateCheckingExercise.cshtml";

    [HttpGet("admin/windows/{id:guid}/exercises/{exerciseId:guid}/edit")]
    public async Task<IActionResult> Edit(Guid id, Guid exerciseId, CancellationToken cancellationToken)
    {
        var window = await windowService.GetByIdAsync(id, cancellationToken);
        var exercise = window?.Exercises.SingleOrDefault(e => e.Id == exerciseId);
        if (window is null || exercise is null) return NotFound();

        var model = new CreateCheckingExerciseItem
        {
            WindowId = id,
            Name = exercise.Name ?? ExerciseLabels.For(exercise.ExerciseType),
            ExerciseType = exercise.ExerciseType,
            TabName = exercise.TabName,
            TabOrder = exercise.TabOrder,
            IsEnabled = exercise.IsEnabled,
            DisplayOnly = exercise.DisplayOnly,
            Layout = exercise.Layout,
            ShowLateResultsWarning = exercise.ShowLateResultsWarning,
            VisibleFrom = exercise.VisibleFrom,
            VisibleUntil = exercise.VisibleUntil,
            ReplacesCheckingExerciseId = exercise.ReplacesCheckingExerciseId,
            Dates = new ExerciseDatesItem
            {
                StartDate = exercise.StartDate,
                StartHour = exercise.StartDate.Hour,
                StartMinute = exercise.StartDate.Minute,
                EndDate = exercise.EndDate,
                EndHour = exercise.EndDate.Hour,
                EndMinute = exercise.EndDate.Minute
            }
        };
        Decorate(model, window, exercise);
        return View(PageView, model);
    }

    [HttpPost("admin/windows/{id:guid}/exercises/{exerciseId:guid}/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(Guid id, Guid exerciseId, CreateCheckingExerciseItem model,
        CancellationToken cancellationToken)
    {
        if (id != model.WindowId) return BadRequest();
        var window = await windowService.GetByIdAsync(id, cancellationToken);
        var exercise = window?.Exercises.SingleOrDefault(e => e.Id == exerciseId);
        if (window is null || exercise is null) return NotFound();

        Decorate(model, window, exercise);
        if (string.IsNullOrWhiteSpace(model.TabName))
            ModelState.AddModelError(nameof(model.TabName), "Enter a tab name");

        if (model.ReplacesCheckingExerciseId is { } replacementId
            && model.ReplacementOptions.All(e => e.Id != replacementId))
            ModelState.AddModelError(nameof(model.ReplacesCheckingExerciseId),
                "Select another exercise from this window that does not already replace this exercise");

        var kindChanged = model.ExerciseType != exercise.ExerciseType;
        if (kindChanged && KindChangeMessage(await windowService.KindChangeBlockAsync(exercise, cancellationToken))
                is { } kindBlocked)
            ModelState.AddModelError(nameof(model.ExerciseType), kindBlocked);

        if (!ModelState.IsValid) return View(PageView, model);

        var updated = new CheckingExerciseDto
        {
            Id = exercise.Id,
            UsesExerciseStorage = exercise.UsesExerciseStorage,
            ExerciseType = model.ExerciseType,
            Name = model.Name!.Trim(),
            TabName = model.TabName!.Trim(),
            TabOrder = model.TabOrder!.Value,
            IsEnabled = model.IsEnabled,
            DisplayOnly = model.DisplayOnly,
            Layout = model.Layout,
            ShowLateResultsWarning = model.ExerciseType == CheckingExerciseType.ResultsEnquiry && model.ShowLateResultsWarning,
            VisibleFrom = model.VisibleFrom,
            VisibleUntil = model.VisibleUntil,
            ReplacesCheckingExerciseId = model.ReplacesCheckingExerciseId,
            StartDate = KeepSeconds(model.Dates.StartDateTime!.Value, exercise.StartDate),
            EndDate = KeepSeconds(model.Dates.EndDateTime!.Value, exercise.EndDate),
            WindowStart = exercise.WindowStart,
            WindowEnd = exercise.WindowEnd,
            Datasets = exercise.Datasets,
            // AB#307773: carried so the window's derived end date keeps the extension ends. The
            // window update never writes extensions; it only reads their dates.
            Extensions = exercise.Extensions,
            ValidatedAt = kindChanged ? null : exercise.ValidatedAt,
            ValidatedIngressChecksum = exercise.ValidatedIngressChecksum,
            ValidatedSchemaChecksum = exercise.ValidatedSchemaChecksum
        };
        // Only a new kind can clash. A window saved before the one-per-kind rule may hold two of a
        // kind already, and each must stay editable so the admin can sort them out.
        if (kindChanged && ExerciseKindClash.Message(updated, window.Exercises) is { } clash)
        {
            ModelState.AddModelError(nameof(model.ExerciseType), clash);
            return View(PageView, model);
        }

        window.Exercises[window.Exercises.IndexOf(exercise)] = updated;
        await windowService.UpdateAsync(window, cancellationToken);
        return RedirectToAction("Index", "Summary", new { id });
    }

    private void Decorate(CreateCheckingExerciseItem model, CheckingWindowDto window, CheckingExerciseDto exercise)
    {
        model.DataExercise = exercise;
        model.IsEditing = true;
        model.WindowTitle = window.Title;
        model.ReplacementOptions = window.Exercises
            .Where(e => !WouldCreateCycle(window, exercise.Id, e.Id))
            .InTabOrder().ToList();
        model.PostUrl = Url.Action("Update", "EditCheckingExercise", new { id = window.Id, exerciseId = exercise.Id });
        model.CancelUrl = Url.Action("Index", "Summary", new { id = window.Id });
    }

    private static bool WouldCreateCycle(CheckingWindowDto window, Guid exerciseId, Guid replacementId)
    {
        var visited = new HashSet<Guid> { exerciseId };
        Guid? current = replacementId;
        while (current is { } id)
        {
            if (!visited.Add(id)) return true;
            current = window.Exercises.SingleOrDefault(e => e.Id == id)?.ReplacesCheckingExerciseId;
        }
        return false;
    }

    private static DateTime KeepSeconds(DateTime posted, DateTime original) =>
        posted == original.AddTicks(-(original.Ticks % TimeSpan.TicksPerMinute)) ? original : posted;

    private static string? KindChangeMessage(KindChangeBlock block) => block switch
    {
        KindChangeBlock.None => null,
        KindChangeBlock.LegacyStorage => "The type cannot be changed for this existing exercise",
        KindChangeBlock.LiveRelease =>
            "The type cannot be changed once this exercise has live data. Add a new exercise instead",
        KindChangeBlock.ChangeRequests =>
            "The type cannot be changed because schools have made requests on this exercise. Add a new exercise instead",
        _ => throw new ArgumentOutOfRangeException(nameof(block), block, null)
    };
}
