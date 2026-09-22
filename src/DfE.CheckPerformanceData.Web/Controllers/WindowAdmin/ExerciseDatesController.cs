using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;
using DfE.CheckPerformanceData.Web.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;

/// <summary>
/// One checking exercise's own dates, while the window is still a draft (#319, #466). There is no
/// window-level date step: the window's StartDate/EndDate is the union of these, derived in
/// <see cref="Application.WindowManagement.CheckingWindowDto.DeriveDatesFromExercises"/>, so the two
/// cannot disagree. Keyed by position in the draft's exercise list rather than by
/// <c>CheckingExerciseType</c> — a display-only exercise has no kind to key on. Editing an existing
/// window's exercise dates is a separate page (Task 8).
/// </summary>
[RequireAdminSection(AdminNavKeys.NewWindow)]
public sealed class ExerciseDatesController : Controller
{
    private const string PageView = "~/Views/WindowAdmin/ExerciseDates.cshtml";

    [HttpGet("admin/windows/exercises/{index:int}/dates")]
    public IActionResult New(int index)
    {
        CheckingWindowDraft? draft = HttpContext.Session.GetObject<CheckingWindowDraft>("CheckingWindowDraft");
        if (draft == null)
        {
            return BadRequest("No draft data");
        }

        ExerciseDraft? target = draft.ExerciseAt(index);
        if (target is null)
        {
            return NotFound();
        }

        return View(PageView, Model(index, target.Name, target.StartDate, target.EndDate,
            Url.Action("Submit", "ExerciseDates", new { index }),
            Url.Action("Index", "CancelCreation")));
    }

    [HttpPost("admin/windows/exercises/{index:int}/dates")]
    [ValidateAntiForgeryToken]
    public IActionResult Submit(int index, ExerciseDatesItem model)
    {
        CheckingWindowDraft? draft = HttpContext.Session.GetObject<CheckingWindowDraft>("CheckingWindowDraft");
        if (draft == null)
        {
            return BadRequest("No draft data");
        }

        ExerciseDraft? target = draft.ExerciseAt(index);
        if (target is null)
        {
            return NotFound();
        }

        Decorate(model, index, target.Name, Url.Action("Submit", "ExerciseDates", new { index }),
            Url.Action("Index", "CancelCreation"));

        if (ModelState.IsValid)
        {
            Validate(model);
        }

        if (!ModelState.IsValid)
        {
            return View(PageView, model);
        }

        target.StartDate = model.StartDateTime;
        target.EndDate = model.EndDateTime;
        HttpContext.Session.SetObject("CheckingWindowDraft", draft);

        return Redirect(draft.NextController(Url));
    }

    private void Validate(ExerciseDatesItem model)
    {
        if (model.StartDateTime < DateTime.UtcNow.Date)
        {
            ModelState.AddModelError(nameof(ExerciseDatesItem.StartDate), "Start date can not occur in the past");
        }

        if (model.EndDateTime < model.StartDateTime)
        {
            ModelState.AddModelError(nameof(ExerciseDatesItem.EndDate), "End date can not occur before the start date");
        }
    }

    private static ExerciseDatesItem Model(
        int index, string label, DateTime? start, DateTime? end, string? postUrl, string? cancelUrl) =>
        new()
        {
            Index = index,
            ExerciseLabel = label,
            StartDate = start,
            StartHour = start?.Hour ?? ExerciseDatesItem.DefaultStartHour,
            StartMinute = start?.Minute ?? 0,
            EndDate = end,
            EndHour = end?.Hour ?? ExerciseDatesItem.DefaultEndHour,
            EndMinute = end?.Minute ?? 0,
            PostUrl = postUrl,
            CancelUrl = cancelUrl
        };

    // The label and the urls are not posted back, so a redisplayed page has to be given them again.
    private static void Decorate(ExerciseDatesItem model, int index, string label, string? postUrl, string? cancelUrl)
    {
        model.Index = index;
        model.ExerciseLabel = label;
        model.PostUrl = postUrl;
        model.CancelUrl = cancelUrl;
    }
}
