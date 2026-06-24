namespace DfE.CheckPerformanceData.Web.ViewComponents;

public class GovUkTextInputViewModel
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Label { get; init; }
    public string? Value { get; init; }
    public string Type { get; init; } = "text";
}