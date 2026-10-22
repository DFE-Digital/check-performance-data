using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.WindowManagement;

/// <summary>
/// One exercise as the school-facing pages see it: a tab, its dates, and what may be done with it.
/// Flat rather than a window plus an exercise, because the pages ask about one tab at a time.
/// </summary>
public sealed record CheckingDataExercise(
    Guid Id, Guid WindowId, string Name, string TabName, int TabOrder,
    CheckingExerciseType? ExerciseType, KeyStages KeyStage,
    bool IsEnabled, DateTime? VisibleFrom, DateTime? VisibleUntil,
    DateTime WindowStart, DateTime WindowEnd, DateTime ActionStart, DateTime ActionEnd,
    Guid? ReplacesCheckingExerciseId, bool UsesExerciseStorage = false, bool DisplayOnly = false,
    Guid? CurrentReleaseId = null)
{
    /// <summary>The tab is drawn. VisibleUntil is exclusive, so a share disappears at the instant
    /// it ends rather than lingering for the rest of that day.</summary>
    public bool IsVisible(DateTime now) => IsEnabled
        && (!VisibleFrom.HasValue || VisibleFrom <= now)
        && (!VisibleUntil.HasValue || VisibleUntil > now);

    /// <summary>
    /// A school may start a journey from this tab. It needs a kind to route to, it must not be
    /// display-only, and both the window and the exercise's own dates must be running. Fails
    /// closed: anything unstated is "no".
    /// </summary>
    public bool CanAct(DateTime now) => ExerciseType is not null && !DisplayOnly && IsVisible(now)
        && WindowStart <= now && WindowEnd >= now
        && ActionStart <= now && ActionEnd >= now;
}

/// <summary>Every visible exercise across every window, in tab order.</summary>
public interface ICheckingDataCatalogue
{
    Task<IReadOnlyList<CheckingDataExercise>> GetVisibleAsync(CancellationToken cancellationToken);
}
