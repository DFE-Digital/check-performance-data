using DfE.CheckPerformanceData.Application.WindowManagement;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;

/// <summary>
/// The "which checking exercises does this window run?" step (#319, #466). The page lists the
/// window type's templates, pre-ticked, plus (on an existing window) any exercise the admin added
/// by hand. <see cref="ExerciseChoice"/> (Application) decides which exercises are on offer and
/// whether a Change here would discard files.
/// </summary>
public sealed class ExercisesItem : AdminPage
{
    /// <summary>Every offered exercise, in display order.</summary>
    public IReadOnlyList<ExerciseChoice> All { get; set; } = [];

    /// <summary>The ticked names. Bound from the checkboxes on post.</summary>
    public List<string> Selected { get; set; } = [];
}
