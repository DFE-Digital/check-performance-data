using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Persistence.Entities.CheckingWindowWorkflow;

public sealed class CheckingWindow
{
    public Guid Id { get; init; }
    public DateOnly StartDate { get; init; }
    public DateOnly EndDate { get; init; }
    public KeyStages KeyStage { get; init; }
    public string Title { get; init; } = string.Empty;
    public ICollection<CheckingWindowStep> CheckingWindowRequestSteps {get;init;} = [];
}

public class CheckingWindowStep
{
    public Guid Id { get; init; }
    public Guid CheckingWindowId { get; init; }
    public RequestTypes RequestType {get; init;} // Add, Include, Remove
    public CheckingWindowStepType StepType {get; init;} // Date, Upload, More Details etc
    public int Order { get; init; } 
    public bool IsRequired {get; init;}
}