namespace DfE.CheckPerformanceData.Application.WindowManagement;

/// <summary>
/// The one order of a window's exercises: tab order, then id. The school tabs, the admin pages,
/// the next steps and the deadlines all use it, so no list disagrees with another. The id breaks
/// a tie the same way everywhere (as ExerciseTabBuilder and CheckingDataCatalogue do).
/// </summary>
public static class ExerciseOrder
{
    public static IOrderedEnumerable<CheckingExerciseDto> InTabOrder(this IEnumerable<CheckingExerciseDto> exercises) =>
        exercises.OrderBy(e => e.TabOrder).ThenBy(e => e.Id);
}
