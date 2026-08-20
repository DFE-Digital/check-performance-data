using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;
using DfE.CheckPerformanceData.Web.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;

/// <summary>
/// One checking exercise's own dates (#319). There is no window-level date step any more: the
/// window's StartDate/EndDate is the union of these, derived in
/// <see cref="CheckingWindowDto.DeriveDatesFromExercises"/>, so the two cannot disagree.
/// </summary>
public sealed class ExerciseDatesController(IWindowService windowService) : Controller
{
    private const string PageView = "~/Views/WindowAdmin/ExerciseDates.cshtml";

    [HttpGet("admin/windows/exercises/{exercise}/dates")]
    public IActionResult New(CheckingExerciseType exercise)
    {
        CheckingWindowDraft? draft = HttpContext.Session.GetObject<CheckingWindowDraft>("CheckingWindowDraft");

        if (draft == null)
        {
            return BadRequest("No draft data");
        }

        ExerciseDraft? target = draft.Exercises.SingleOrDefault(e => e.ExerciseType == exercise);

        if (target is null)
        {
            return NotFound();
        }

        return View(PageView, Model(Guid.Empty, exercise, target.StartDate, target.EndDate,
            Url.Action("Submit", "ExerciseDates", new { exercise }),
            Url.Action("Index", "CancelCreation")));
    }

    [HttpPost("admin/windows/exercises/{exercise}/dates")]
    [ValidateAntiForgeryToken]
    public IActionResult Submit(CheckingExerciseType exercise, ExerciseDatesItem model)
    {
        CheckingWindowDraft? draft = HttpContext.Session.GetObject<CheckingWindowDraft>("CheckingWindowDraft");

        if (draft == null)
        {
            return BadRequest("No draft data");
        }

        ExerciseDraft? target = draft.Exercises.SingleOrDefault(e => e.ExerciseType == exercise);

        if (target is null)
        {
            return NotFound();
        }

        Decorate(model, exercise, Url.Action("Submit", "ExerciseDates", new { exercise }),
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

    [HttpGet("admin/windows/{id:guid}/exercises/{exercise}/dates")]
    public async Task<IActionResult> Edit(Guid id, CheckingExerciseType exercise, CancellationToken cancellationToken)
    {
        CheckingWindowDto? window = await windowService.GetByIdAsync(id, cancellationToken);
        CheckingExerciseDto? target = window?.FindExercise(exercise);

        if (target is null)
        {
            return NotFound();
        }

        return View(PageView, Model(id, exercise, target.StartDate, target.EndDate,
            Url.Action("Update", "ExerciseDates", new { id, exercise }),
            Url.Action("Index", "Summary", new { id })));
    }

    [HttpPost("admin/windows/{id:guid}/exercises/{exercise}/dates")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(
        Guid id, CheckingExerciseType exercise, ExerciseDatesItem model, CancellationToken cancellationToken)
    {
        CheckingWindowDto? window = await windowService.GetByIdAsync(id, cancellationToken);
        CheckingExerciseDto? target = window?.FindExercise(exercise);

        if (window is null || target is null)
        {
            return NotFound();
        }

        Decorate(model, exercise, Url.Action("Update", "ExerciseDates", new { id, exercise }),
            Url.Action("Index", "Summary", new { id }));

        if (ModelState.IsValid)
        {
            Validate(model);
        }

        if (!ModelState.IsValid)
        {
            return View(PageView, model);
        }

        target.StartDate = model.StartDateTime!.Value;
        target.EndDate = model.EndDateTime!.Value;

        // UpdateAsync re-derives the window's outer pair from every exercise, so moving one
        // exercise's end past the window's own end widens the window rather than being rejected.
        await windowService.UpdateAsync(window, cancellationToken);

        return RedirectToAction("Index", "Summary", new { id });
    }

    private void Validate(ExerciseDatesItem model)
    {
        if (model.StartDateTime < DateTime.UtcNow.Date)
        {
            ModelState.AddModelError(nameof(ExerciseDatesItem.StartDate), "Start date can not occur in the past.");
        }

        if (model.EndDateTime < model.StartDateTime)
        {
            ModelState.AddModelError(nameof(ExerciseDatesItem.EndDate), "End date can not occur before the start date.");
        }
    }

    private static ExerciseDatesItem Model(
        Guid windowId, CheckingExerciseType exercise, DateTime? start, DateTime? end,
        string? postUrl, string? cancelUrl) =>
        new()
        {
            WindowId = windowId,
            ExerciseType = exercise,
            ExerciseLabel = ExerciseLabels.For(exercise),
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
    private static void Decorate(
        ExerciseDatesItem model, CheckingExerciseType exercise, string? postUrl, string? cancelUrl)
    {
        model.ExerciseType = exercise;
        model.ExerciseLabel = ExerciseLabels.For(exercise);
        model.PostUrl = postUrl;
        model.CancelUrl = cancelUrl;
    }
}
