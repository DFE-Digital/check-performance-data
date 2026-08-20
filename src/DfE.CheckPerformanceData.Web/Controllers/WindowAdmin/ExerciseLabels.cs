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
}
