namespace DfE.CheckPerformanceData.Application.Journey;

/// <summary>
/// A named, externally-evaluated condition referenced by a question option's
/// <see cref="QuestionOption.VisibleWhen"/>. Register one implementation per
/// condition in DI.
/// </summary>
public interface IJourneyCondition
{
    /// <summary>The name referenced from JSON, e.g. "SchoolIsIndependent".</summary>
    string Name { get; }

    bool Evaluate(JourneyConditionContext ctx);
}
