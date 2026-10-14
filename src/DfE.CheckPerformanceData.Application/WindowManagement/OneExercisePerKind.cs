using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.WindowManagement;

/// <summary>
/// A window has at most one exercise of each kind: one pupil data checking, one results enquiry.
/// Everything that addresses "this window, this kind" (<see cref="CheckingWindowDto.FindExercise"/>,
/// the kind-keyed admin routes, the journeys) relies on it.
/// </summary>
/// <remarks>
/// The rule ignores IsEnabled and the visibility dates: a disabled second exercise of a kind still
/// makes the kind lookups ambiguous. A data share has no kind and never clashes.
/// </remarks>
public static class OneExercisePerKind
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
        a.ExerciseType is CheckingExerciseType kind && b.ExerciseType == kind;
}
