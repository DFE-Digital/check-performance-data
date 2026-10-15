namespace DfE.CheckPerformanceData.Application.WindowManagement;

/// <summary>What schools can see and do with one checking exercise now. Shown to admins as one tag
/// per exercise on the window summary, from <see cref="ICheckingExerciseService.StatusOf"/>.</summary>
public enum ExerciseSchoolStatus
{
    /// <summary>Schools do not see it: disabled, outside its visibility dates, or the window itself
    /// is outside its dates.</summary>
    Hidden,

    /// <summary>Schools see it. For an exercise with a journey, they can also make changes.</summary>
    Visible,

    /// <summary>Schools see it but cannot make changes: the time is outside the exercise's own dates.</summary>
    VisibleClosed
}
