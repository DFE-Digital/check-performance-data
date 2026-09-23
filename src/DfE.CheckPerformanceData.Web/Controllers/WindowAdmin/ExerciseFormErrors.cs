using DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;

namespace DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;

/// <summary>
/// Maps an <see cref="Application.WindowManagement.ExerciseChangeResult"/> refusal reason onto the
/// <see cref="ExerciseFormItem"/> field its error summary link should point at (#466). The service
/// owns the wording — this only decides which control gets the error, so a reason with no explicit
/// row still shows (on Name) rather than being swallowed. Shared by AddExerciseController and
/// EditExerciseController.
/// </summary>
public static class ExerciseFormErrors
{
    /// <summary>
    /// True when the refusal means the window or exercise itself is gone — a race with someone
    /// else's edit, not something an admin can fix by changing a field. A caller redirects to
    /// Summary instead of rendering the form with this reason: rendering it would either put
    /// "Window not found" under the Name label (Add, where nothing else fits) or, on a re-post of
    /// an already-removed exercise, discard the error and 404 anyway once the redisplay's own
    /// lookup finds nothing.
    /// </summary>
    public static bool IsMissing(string? reason) =>
        reason is "Window not found" or "Exercise not found";

    /// <summary>Only ever called with a reason an admin can act on — <see cref="IsMissing"/> is
    /// checked first and redirects instead.</summary>
    public static string FieldFor(string? reason) => reason switch
    {
        "Enter a tab name" => nameof(ExerciseFormItem.TabName),
        "End date can not occur before the start date" => nameof(ExerciseFormItem.EndDate),
        _ when reason?.StartsWith("Tab name must be", StringComparison.Ordinal) == true => nameof(ExerciseFormItem.TabName),
        _ => nameof(ExerciseFormItem.Name)
    };
}
