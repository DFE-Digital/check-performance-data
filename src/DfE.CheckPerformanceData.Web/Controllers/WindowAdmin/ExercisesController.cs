using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;
using DfE.CheckPerformanceData.Web.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;

/// <summary>
/// "Which checking exercises does this window run?" (#319). Every <see cref="CheckingExerciseType"/>
/// is listed, pre-ticked from the window type's defaults, so a new member of the enum surfaces here
/// with no change to this controller — while a single-exercise window is still one Continue.
/// </summary>
public sealed class ExercisesController(IWindowService windowService) : Controller
{
    private const string PageView = "~/Views/WindowAdmin/Exercises.cshtml";
    private const string NothingSelected = "Select at least one checking exercise";

    [HttpGet("admin/windows/exercises")]
    public IActionResult New()
    {
        CheckingWindowDraft? draft = HttpContext.Session.GetObject<CheckingWindowDraft>("CheckingWindowDraft");

        if (draft == null)
        {
            return BadRequest("No draft data");
        }

        // Pre-ticked from the type on the first visit; on a revisit the admin's own choice wins,
        // otherwise coming back to change one box would silently reset the others.
        List<CheckingExerciseType> selected = draft.Exercises.Count > 0
            ? draft.Exercises.OrderBy(e => e.SortOrder).Select(e => e.ExerciseType).ToList()
            : DefaultsFor(draft.CheckingWindowType);

        return View(PageView, new ExercisesItem
        {
            All = AllExercises,
            Selected = selected,
            PostUrl = Url.Action("Submit", "Exercises"),
            CancelUrl = Url.Action("Index", "CancelCreation")
        });
    }

    [HttpPost("admin/windows/exercises")]
    [ValidateAntiForgeryToken]
    public IActionResult Submit(ExercisesItem model)
    {
        CheckingWindowDraft? draft = HttpContext.Session.GetObject<CheckingWindowDraft>("CheckingWindowDraft");

        if (draft == null)
        {
            return BadRequest("No draft data");
        }

        if (model.Selected.Count == 0)
        {
            ModelState.AddModelError(nameof(ExercisesItem.Selected), NothingSelected);
            return View(PageView, Redisplay(model, Url.Action("Submit", "Exercises"), Url.Action("Index", "CancelCreation")));
        }

        // Dates already given for an exercise that is still ticked survive, so changing the tick
        // list does not send the admin back through date pages they have already filled in.
        draft.Exercises = model.Selected
            .Distinct()
            .OrderBy(WindowExercises.SortOrderFor)
            .Select(type => draft.Exercises.SingleOrDefault(e => e.ExerciseType == type)
                            ?? new ExerciseDraft { ExerciseType = type })
            .Select(e =>
            {
                e.SortOrder = WindowExercises.SortOrderFor(e.ExerciseType);
                return e;
            })
            .ToList();

        HttpContext.Session.SetObject("CheckingWindowDraft", draft);

        return Redirect(draft.NextController(Url));
    }

    [HttpGet("admin/windows/{id:guid}/exercises")]
    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken)
    {
        CheckingWindowDto? window = await windowService.GetByIdAsync(id, cancellationToken);

        if (window is null)
        {
            return NotFound();
        }

        return View(PageView, new ExercisesItem
        {
            WindowId = id,
            All = AllExercises,
            Selected = window.Exercises.OrderBy(e => e.SortOrder).Select(e => e.ExerciseType).ToList(),
            WithFiles = window.Exercises.Where(e => e.Datasets.Any(d => d.IsComplete))
                .Select(e => e.ExerciseType).ToList(),
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

        List<CheckingExerciseType> wanted = model.Selected.Distinct().OrderBy(WindowExercises.SortOrderFor).ToList();

        // A newly ticked exercise starts on the window's own dates. That is a placeholder the admin
        // then edits, not an answer — but it means the window is never left holding an exercise with
        // no dates at all, which the union that derives the outer pair could not survive.
        window.Exercises = wanted
            .Select(type => window.FindExercise(type) ?? new CheckingExerciseDto
            {
                ExerciseType = type,
                StartDate = window.StartDate,
                EndDate = window.EndDate,
                SortOrder = WindowExercises.SortOrderFor(type)
            })
            .ToList();

        await windowService.UpdateAsync(window, cancellationToken);

        return RedirectToAction("Index", "Summary", new { id });
    }

    private static IReadOnlyList<CheckingExerciseType> AllExercises =>
        Enum.GetValues<CheckingExerciseType>().OrderBy(WindowExercises.SortOrderFor).ToList();

    private static List<CheckingExerciseType> DefaultsFor(CheckingWindowType? type) =>
        type is null ? [] : WindowExercises.DefaultsFor(type.Value).ToList();

    private static ExercisesItem Redisplay(
        ExercisesItem model, string? postUrl, string? cancelUrl, Guid windowId = default,
        CheckingWindowDto? window = null) => new()
    {
        WindowId = windowId,
        All = AllExercises,
        Selected = model.Selected,
        WithFiles = window is null
            ? []
            : window.Exercises.Where(e => e.Datasets.Any(d => d.IsComplete))
                .Select(e => e.ExerciseType).ToList(),
        PostUrl = postUrl,
        CancelUrl = cancelUrl
    };
}
