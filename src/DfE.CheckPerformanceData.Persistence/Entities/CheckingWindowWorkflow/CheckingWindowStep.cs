using System.Text.Json;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Persistence.Entities.CheckingWindowWorkflow;

public enum CheckingWindowStepType
{
    Date,
    EvidenceUpload,
    FurtherDetails,
    CheckBox
}

public enum CheckingWindowRequestType
{
    Add,
    Include,
    Remove
}

public class CheckingWindowDefinition
{
    public Guid Id { get; init; }
    public CheckingWindowType WindowType { get; init; }
    public string Title { get; init; } = string.Empty;
    public CheckingWindowRequestType RequestType { get; init; }
    public IReadOnlyList<CheckingWindowStep> Steps { get; init; } = new List<CheckingWindowStep>();
}

public enum CheckingWindowType
{
    KS2,
    KS4June,
    KS4Autumn,
    Post16
}

public class CheckingWindowStep
{
    public Guid Id { get; init; }
    public Guid CheckingWindowDefinitionId { get; init; }
    public CheckingWindowStepType CheckingWindowStepType { get; init; }
    public string Title { get; init; } = string.Empty;
    public int Order { get; init; }
    public bool IsRequired { get; init; }
}

public class AmendmentRequest
{
    public Guid Id { get; init; }
    public Guid CheckingWindowDefinitionId { get; init; }
    public Guid CheckingWindowId { get; init; }
    public string LearnerId { get; init; } = string.Empty;
    public CheckingWindowRequestType RequestType { get; init; }
    public int CurrentStepIndex  { get; set; }
    public AmendmentStatus Status { get; set; }
    public ICollection<StepResponse> StepResponses { get; init; } = new List<StepResponse>();
}

public class StepResponse
{
    public Guid Id { get; init; }
    public Guid AmendmentRequestId { get; init; }
    public CheckingWindowStepType StepType { get; init; }
    public int StepIndex { get; init; }
    public JsonDocument ResponseData { get; init; } = JsonDocument.Parse("{}");
    public DateTime CompletedAt { get; set; }
}

public enum AmendmentStatus
{
    InProgress,
    Submitted,
    Rejected,
    Approved
}
