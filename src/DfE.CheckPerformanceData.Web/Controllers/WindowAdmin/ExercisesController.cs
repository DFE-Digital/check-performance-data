using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;
using DfE.CheckPerformanceData.Web.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;

/// <summary>
/// "Which checking exercises does this window run?" (#319, #466). The page lists the window type's
/// templates and pre-ticks all of them; on an existing window it also lists any exercise the admin
/// added by hand, so those can be unticked (removed) here too. Anything else will be added from
/// Summary (Task 8).
/// </summary>
[RequireAdminSection(AdminNavKeys.ManageWindow)]
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

        IReadOnlyList<ExerciseTemplate> templates = TemplatesFor(draft);

        // Pre-ticked from the type on the first visit; on a revisit the admin's own choice wins,
        // otherwise coming back to change one box would silently reset the others.
        List<string> selected = draft.Exercises.Count > 0
            ? draft.Exercises.OrderBy(e => e.SortOrder).Select(e => e.Name).ToList()
            : templates.Select(t => t.Name).ToList();

        return View(PageView, new ExercisesItem
        {
            All = WindowExercises.ChoicesFor(templates),
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

        IReadOnlyList<ExerciseTemplate> templates = TemplatesFor(draft);
        List<ExerciseTemplate> wanted = templates.Where(t => model.Selected.Contains(t.Name)).ToList();

        if (wanted.Count == 0)
        {
            ModelState.AddModelError(nameof(ExercisesItem.Selected), NothingSelected);
            return View(PageView, new ExercisesItem
            {
                All = WindowExercises.ChoicesFor(templates),
                Selected = model.Selected,
                PostUrl = Url.Action("Submit", "Exercises"),
                CancelUrl = Url.Action("Index", "CancelCreation")
            });
        }

        // An exercise that stays ticked keeps the dates already given for it.
        draft.Exercises = wanted
            .Select(t => draft.Exercises.SingleOrDefault(e => e.Name == t.Name) ?? ExerciseDraft.From(t))
            .OrderBy(e => e.SortOrder)
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
            All = WindowExercises.ChoicesFor(window),
            Selected = window.Exercises.OrderBy(e => e.SortOrder).Select(e => e.Name).ToList(),
            PostUrl = Url.Action("Update", "Exercises", new { id }),
            CancelUrl = Url.Action("Index", "Summary", new { id })
        });
    }

    [HttpPost("admin/windows/{id:guid}/exercises")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(Guid id, ExercisesItem model, CancellationToken cancellationToken)
    {
        // The rule — which exercises may be dropped, which templates get added, on what dates —
        // lives in WindowService.SetExercisesAsync, not here: the Edit page must refuse a removal
        // exactly as ExercisesController's other caller (the future per-exercise Remove) does.
        ExerciseChangeResult result = await windowService.SetExercisesAsync(id, model.Selected, cancellationToken);
        if (result.Succeeded)
        {
            return RedirectToAction("Index", "Summary", new { id });
        }

        CheckingWindowDto? window = await windowService.GetByIdAsync(id, cancellationToken);
        if (window is null)
        {
            return NotFound();
        }

        ModelState.AddModelError(nameof(ExercisesItem.Selected), result.Reason!);
        return View(PageView, new ExercisesItem
        {
            WindowId = id,
            All = WindowExercises.ChoicesFor(window),
            Selected = model.Selected,
            PostUrl = Url.Action("Update", "Exercises", new { id }),
            CancelUrl = Url.Action("Index", "Summary", new { id })
        });
    }

    private static IReadOnlyList<ExerciseTemplate> TemplatesFor(CheckingWindowDraft draft) =>
        draft.CheckingWindowType is null ? [] : WindowExercises.DefaultsFor(draft.CheckingWindowType.Value);
}
