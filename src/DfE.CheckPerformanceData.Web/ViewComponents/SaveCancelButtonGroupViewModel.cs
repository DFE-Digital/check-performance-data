namespace DfE.CheckPerformanceData.Web.ViewComponents;

public sealed class SaveCancelButtonGroupViewModel
{
    public string SaveText { get; init; } = "Save";
    public string CancelText { get; init; } = "Cancel";
    public required string CancelHref { get; init; }
}