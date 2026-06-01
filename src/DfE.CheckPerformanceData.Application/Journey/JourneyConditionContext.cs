namespace DfE.CheckPerformanceData.Application.Journey;

/// <summary>Everything a journey condition may inspect.</summary>
public sealed class JourneyConditionContext
{
    public required RequestState Journey { get; init; }
    public required JourneyUserContext User { get; init; }
}
