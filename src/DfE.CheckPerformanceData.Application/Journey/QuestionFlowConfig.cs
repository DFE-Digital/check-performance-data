namespace DfE.CheckPerformanceData.Application.Journey;

public sealed class QuestionFlowConfig
{
    public required string FirstPageId { get; init; }
    public required List<JourneyPage> Pages { get; init; }
}
