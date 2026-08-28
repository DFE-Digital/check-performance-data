using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;

/// <summary>
/// The "which checking exercises does this window run?" step (#319). Every
/// <see cref="CheckingExerciseType"/> is offered, pre-ticked from the window type's defaults, so a
/// new member of the enum appears here without this page being touched.
/// </summary>
public sealed class ExercisesItem : AdminPage
{
    /// <summary>Every exercise type, in display order.</summary>
    public IReadOnlyList<CheckingExerciseType> All { get; set; } = [];

    /// <summary>The ticked ones. Bound from the checkboxes on post.</summary>
    public List<CheckingExerciseType> Selected { get; set; } = [];

    /// <summary>
    /// Exercises that already hold ingress files. Unticking one throws those files away, so the
    /// page warns rather than doing it silently.
    /// </summary>
    public IReadOnlyList<CheckingExerciseType> WithFiles { get; set; } = [];
}
