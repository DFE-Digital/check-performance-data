using DfE.CheckPerformanceData.Application.WindowManagement;

namespace DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;

/// <summary>
/// The form error for <see cref="LiveExercises.FindClash"/>, shared by the add and edit exercise
/// pages. The rule is in Application; only the wording is here.
/// </summary>
internal static class LiveExerciseClash
{
    public static string? Message(CheckingExerciseDto exercise, IEnumerable<CheckingExerciseDto> exercises) =>
        LiveExercises.FindClash(exercise, exercises) is { } other
            ? $"{other.Name ?? other.TabName} is already enabled as {ExerciseLabels.For(exercise.ExerciseType)} " +
              "in this window. Disable one of them, or set visibility dates that do not overlap"
            : null;
}
