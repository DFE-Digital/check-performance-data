using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;
using DfE.CheckPerformanceData.Web.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;

/// <summary>
/// "Which checking exercises does this window run?" (#319), for an existing window. Every
/// <see cref="CheckingExerciseType"/> is listed, so a new member of the enum surfaces here with no
/// change to this controller. The create wizard has no exercise step: a new window takes its
/// type's defaults (<see cref="CheckingWindowDraft.UseDefaultExercises"/>).
/// </summary>
[RequireAdminSection(AdminNavKeys.ManageWindow)]
public sealed class ExercisesController(IWindowService windowService) : Controller
{
    private const string PageView = "~/Views/WindowAdmin/Exercises.cshtml";
    private const string NothingSelected = "Select at least one checking exercise";

    [HttpGet("admin/windows/{id:guid}/exercises")]
    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken)
    {
        CheckingWindowDto? window = await windowService.GetByIdAsync(id, cancellationToken);

        if (window is null)
        {
            return NotFound();
        }

        // This page ticks and unticks kinds (#319); a display-only exercise (#466) has no kind to
        // tick and is left off both lists — it is added/removed/edited through its own routes.
        return View(PageView, new ExercisesItem
        {
            WindowId = id,
            All = AllExercises,
            Selected = window.Exercises.Where(e => e.ExerciseType is not null)
                .InTabOrder().Select(e => e.ExerciseType.Value).ToList(),
            WithFiles = window.Exercises.Where(e => e.ExerciseType is not null && e.Datasets.Any(d => d.IsComplete))
                .Select(e => e.ExerciseType.Value).ToList(),
            PostUrl = Url.Action("Update", "Exercises", new { id }),
            CancelUrl = Url.Action("Index", "Summary", new { id })
        });
    }

    [HttpPost("admin/windows/{id:guid}/exercises")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(Guid id, ExercisesItem model, CancellationToken cancellationToken)
    {
        CheckingWindowDto? window = await windowService.GetByIdAsync(id, cancellationToken);

        if (window is null)
        {
            return NotFound();
        }

        if (model.Selected.Count == 0)
        {
            ModelState.AddModelError(nameof(ExercisesItem.Selected), NothingSelected);
            return View(PageView, Redisplay(model, Url.Action("Update", "Exercises", new { id }),
                Url.Action("Index", "Summary", new { id }), id, window));
        }

        List<CheckingExerciseType> wanted = model.Selected.Distinct().OrderBy(WindowExercises.DefaultTabOrder).ToList();

        // A newly ticked exercise starts on the window's own dates. That is a placeholder the admin
        // then edits, not an answer — but it means the window is never left holding an exercise with
        // no dates at all, which the union that derives the outer pair could not survive.
        window.Exercises = wanted
            .Select(type => window.FindExercise(type) ?? new CheckingExerciseDto
            {
                ExerciseType = type,
                // Disabled, like a wizard exercise: schools see it once an admin enables it.
                TabName = WindowExercises.DefaultTabName(window.CheckingWindowType, type),
                ShowLateResultsWarning = WindowExercises.ShowsLateResultsWarningByDefault(type),
                Layout = WindowExercises.DefaultLayout(window.CheckingWindowType, type),
                StartDate = window.StartDate,
                EndDate = window.EndDate,
                TabOrder = WindowExercises.DefaultTabOrder(type)
            })
            .ToList();

        await windowService.UpdateAsync(window, cancellationToken);

        return RedirectToAction("Index", "Summary", new { id });
    }

    private static IReadOnlyList<CheckingExerciseType> AllExercises =>
        Enum.GetValues<CheckingExerciseType>().OrderBy(WindowExercises.DefaultTabOrder).ToList();

    private static ExercisesItem Redisplay(
        ExercisesItem model, string? postUrl, string? cancelUrl, Guid windowId = default,
        CheckingWindowDto? window = null) => new()
    {
        WindowId = windowId,
        All = AllExercises,
        Selected = model.Selected,
        WithFiles = window is null
            ? []
            : window.Exercises.Where(e => e.ExerciseType is not null && e.Datasets.Any(d => d.IsComplete))
                .Select(e => e.ExerciseType.Value).ToList(),
        PostUrl = postUrl,
        CancelUrl = cancelUrl
    };
}
