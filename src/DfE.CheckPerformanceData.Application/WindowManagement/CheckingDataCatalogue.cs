using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.WindowManagement;

public sealed record CheckingDataExercise(
    Guid Id, Guid WindowId, string Name, string Stage, string TabName, int TabOrder,
    CheckingDataType DataType, CheckingExerciseType ExerciseType, KeyStages KeyStage,
    bool IsEnabled, DateTime? VisibleFrom, DateTime? VisibleUntil,
    DateTime WindowStart, DateTime WindowEnd, DateTime ActionStart, DateTime ActionEnd,
    Guid? ReplacesCheckingExerciseId, bool UsesExerciseStorage = false)
{
    public bool IsVisible(DateTime now) => IsEnabled
        && (!VisibleFrom.HasValue || VisibleFrom <= now)
        && (!VisibleUntil.HasValue || VisibleUntil > now);

    public bool CanAct(DateTime now) => IsVisible(now)
        && WindowStart <= now && WindowEnd >= now
        && ActionStart <= now && ActionEnd >= now;
}

public interface ICheckingDataCatalogue
{
    Task<IReadOnlyList<CheckingDataExercise>> GetVisibleAsync(CancellationToken cancellationToken);
}
