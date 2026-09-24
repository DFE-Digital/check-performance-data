using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.WindowManagement;

/// <summary>
/// A window must never have two live exercises of one kind. The journeys address "this window,
/// this kind", so with two live candidates they could not tell which exercise's data to use.
/// </summary>
/// <remarks>
/// Two exercises clash when both are enabled, share a kind, and their visibility ranges overlap.
/// A null bound is open-ended, and VisibleUntil is exclusive, so one release may end at the
/// instant the next begins. A data share has no kind and never clashes.
/// </remarks>
public static class LiveExercises
{
    /// <summary>The first other exercise in <paramref name="exercises"/> that clashes with
    /// <paramref name="exercise"/>, or null.</summary>
    public static CheckingExerciseDto? FindClash(CheckingExerciseDto exercise,
        IEnumerable<CheckingExerciseDto> exercises) =>
        exercises.FirstOrDefault(other => !ReferenceEquals(other, exercise)
            && (other.Id == Guid.Empty || other.Id != exercise.Id)
            && Clash(exercise, other));

    /// <summary>Any clashing pair in the list, or null.</summary>
    public static (CheckingExerciseDto First, CheckingExerciseDto Second)? FindAnyClash(
        IReadOnlyList<CheckingExerciseDto> exercises)
    {
        for (var i = 0; i < exercises.Count; i++)
        for (var j = i + 1; j < exercises.Count; j++)
            if (Clash(exercises[i], exercises[j]))
                return (exercises[i], exercises[j]);
        return null;
    }

    private static bool Clash(CheckingExerciseDto a, CheckingExerciseDto b) =>
        a.IsEnabled && b.IsEnabled
        && a.ExerciseType is CheckingExerciseType kind && b.ExerciseType == kind
        && (a.VisibleFrom ?? DateTime.MinValue) < (b.VisibleUntil ?? DateTime.MaxValue)
        && (b.VisibleFrom ?? DateTime.MinValue) < (a.VisibleUntil ?? DateTime.MaxValue);
}
