namespace DfE.CheckPerformanceData.Application.Journey;

/// <summary>
/// A named fact that a flow file tests in <c>visibleWhen</c>, <c>optionalWhen</c> or
/// <c>requireAtLeastOneWhen</c>. Use one only for a fact that the journey's answers do not hold
/// (for example the school type). Do not write a "not" or an "or" fact: the flow file uses
/// <c>"is": false</c> and <c>anyOf</c> for these. A test on an answer is an answer test in the flow file
/// (<see cref="JourneyConditionRef"/>), not a class. Register one implementation per condition in DI.
/// </summary>
public interface IJourneyCondition
{
    /// <summary>The name referenced from JSON, e.g. "school.isIndependent".</summary>
    string Name { get; }

    bool Evaluate(JourneyConditionContext ctx);
}
