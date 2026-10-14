using DfE.CheckPerformanceData.Application.WindowManagement;

namespace DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;

/// <summary>
/// The form error for <see cref="OneExercisePerKind.FindClash"/>, shared by the add and edit
/// exercise pages. The rule is in Application; only the wording is here.
/// </summary>
internal static class ExerciseKindClash
{
    public static string? Message(CheckingExerciseDto exercise, IEnumerable<CheckingExerciseDto> exercises) =>
        OneExercisePerKind.FindClash(exercise, exercises) is { } other
            ? $"This window already has a {ExerciseLabels.For(exercise.ExerciseType).ToLowerInvariant()} " +
              $"exercise, {other.Name ?? other.TabName}. A window can have only one of each kind"
            : null;
}
