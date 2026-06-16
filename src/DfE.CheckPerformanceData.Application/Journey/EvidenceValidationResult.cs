namespace DfE.CheckPerformanceData.Application.Journey;

public sealed class EvidenceValidationResult
{
    public required IReadOnlyList<string> Messages { get; init; }
}
