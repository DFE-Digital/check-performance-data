using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;

public sealed class CheckingWindowDraft : AdminPage
{
    public string? Title { get; set; }
    public string TitleLink(IUrlHelper url) => url.Action("New", "Title")!;
    public CheckingWindowType? CheckingWindowType { get; set; }
    public string CheckingWindowTypeLink(IUrlHelper url) => url.Action("New", "WindowType")!;
    public KeyStages? KeyStage { get; set; }
    public string KeyStageLink(IUrlHelper url) => url.Action("New", "KeyStage")!;

    /// <summary>
    /// The exercises this window will run, each with its own dates (#319). The window's own
    /// StartDate/EndDate is derived from these — the wizard has no window-level date step, so the
    /// outer pair and the exercises can never disagree.
    /// </summary>
    public List<ExerciseDraft> Exercises { get; set; } = [];

    public string ExercisesLink(IUrlHelper url) => url.Action("New", "Exercises")!;

    /// <summary>Earliest exercise start. Null until at least one exercise has its dates.</summary>
    public DateTime? StartDate =>
        Exercises.All(e => e.StartDate.HasValue) && Exercises.Count > 0
            ? Exercises.Min(e => e.StartDate!.Value)
            : null;

    /// <summary>Latest exercise end. Null until at least one exercise has its dates.</summary>
    public DateTime? EndDate =>
        Exercises.All(e => e.EndDate.HasValue) && Exercises.Count > 0
            ? Exercises.Max(e => e.EndDate!.Value)
            : null;

    /// <summary>The first exercise still missing its dates, or null when all are complete.</summary>
    public ExerciseDraft? FirstUndatedExercise =>
        Exercises.OrderBy(e => e.SortOrder).FirstOrDefault(e => !e.IsDated);

    public bool IsValid
    {
        get
        {
            if (IsEmpty || Exercises.Count == 0 || FirstUndatedExercise is not null)
                return false;

            // Each exercise must be a sane range in its own right. The outer pair is their union,
            // so checking the parts is what makes the whole right.
            return Exercises.All(e =>
                e.StartDate!.Value >= DateTime.UtcNow.Date && e.EndDate!.Value >= e.StartDate!.Value);
        }
    }

    public bool IsEmpty =>
        Title == null && !CheckingWindowType.HasValue && !KeyStage.HasValue && Exercises.Count == 0;

    /// <summary>
    /// The next unanswered step. The exercise step comes after the window type, because the type
    /// decides which exercises start ticked; the per-exercise date pages then follow one at a time.
    /// </summary>
    public string NextController(IUrlHelper url)
    {
        if (Title is null) return url.Action("New", "Title")!;
        if (!CheckingWindowType.HasValue) return url.Action("New", "WindowType")!;
        if (!KeyStage.HasValue) return url.Action("New", "KeyStage")!;
        if (Exercises.Count == 0) return url.Action("New", "Exercises")!;

        ExerciseDraft? undated = FirstUndatedExercise;
        return undated is null
            ? url.Action("New", "CreateCheckingWindow")!
            : url.Action("New", "ExerciseDates", new { exercise = undated.ExerciseType })!;
    }

    /// <summary>The draft's exercises as DTOs, ready for <see cref="IWindowService.CreateAsync"/>.</summary>
    public List<CheckingExerciseDto> ToExerciseDtos() =>
        Exercises
            .OrderBy(e => e.SortOrder)
            .Select(e => new CheckingExerciseDto
            {
                ExerciseType = e.ExerciseType,
                StartDate = e.StartDate!.Value,
                EndDate = e.EndDate!.Value,
                SortOrder = e.SortOrder
            })
            .ToList();
}

/// <summary>One ticked checking exercise and its dates, while the window is still a draft.</summary>
public sealed class ExerciseDraft
{
    public CheckingExerciseType ExerciseType { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public int SortOrder { get; set; }

    public bool IsDated => StartDate.HasValue && EndDate.HasValue;
}
