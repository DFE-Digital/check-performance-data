namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;

public sealed class RemoveExerciseViewModel
{
    public required Guid WindowId { get; init; }
    public required Guid ExerciseId { get; init; }
    public required string WindowTitle { get; init; }
    public required string ExerciseLabel { get; init; }
    /// <summary>Ingress and schema files the exercise's slots hold. Removing discards them.</summary>
    public required int FileCount { get; init; }
    /// <summary>The service's reason when a remove was refused; null on first view.</summary>
    public string? Refusal { get; init; }
    public string PostUrl => $"/admin/windows/{WindowId}/exercises/{ExerciseId}/remove";
    public string CancelLink => $"/admin/windows/summary/{WindowId}";
}
