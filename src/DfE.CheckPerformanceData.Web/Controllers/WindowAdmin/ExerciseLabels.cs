using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Extensions;

namespace DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;

/// <summary>
/// Display names for checking exercises in the admin wizard (#319). Read from the enum's own
/// [Display] attribute rather than a table here, so a new exercise type is labelled the moment it
/// is declared — the same reason the wizard lists the enum instead of a hand-kept set.
/// </summary>
public static class ExerciseLabels
{
    public static string For(CheckingExerciseType exercise) => exercise.GetDisplayName();

    /// <summary>A display-only data share (#466) has no kind to label from the enum, so it falls
    /// back to a generic label here. The admin's own name for the exercise is projected separately
    /// (<see cref="Application.WindowManagement.CheckingExerciseDto.Name"/>) and is not this method's
    /// concern.</summary>
    public static string For(CheckingExerciseType? exercise) =>
        exercise is { } type ? For(type) : "Data share";

    /// <summary>
    /// The school-facing label, which names a learner on the pupil-data exercise and so takes the
    /// window's noun — "Student data checking" on 16-19. The parameterless overload above keeps the
    /// enum's display name for admin screens, whose vocabulary does not change per window.
    /// </summary>
    public static string For(CheckingExerciseType exercise, LearnerNoun noun) => exercise switch
    {
        CheckingExerciseType.PupilData => $"{noun.SingularCapitalised} data checking",
        _ => For(exercise)
    };
}
