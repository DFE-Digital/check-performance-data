namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;

/// <summary>The confirmation page before a data file slot is retired, or put back in use.</summary>
public sealed class RetireDatasetViewModel
{
    /// <summary>True to retire the slot; false to put a retired slot back in use.</summary>
    public required bool Retire { get; init; }
    public required string WindowTitle { get; init; }
    public required string ExerciseName { get; init; }
    public required string DatasetLabel { get; init; }
    public required string PostUrl { get; init; }
    public required string CancelLink { get; init; }

    public string Heading => Retire ? $"Retire {DatasetLabel}" : $"Put {DatasetLabel} back in use";
}
