namespace DfE.CheckPerformanceData.Application.Journey;

public interface IQuestionOptionalityService
{
    /// <summary>
    /// Ids of the page's questions whose <see cref="Question.OptionalWhen"/> conditions
    /// are all registered and true for this context — those questions are validated as
    /// optional. Questions without OptionalWhen are never returned; an unregistered
    /// condition name leaves the question mandatory (fail closed).
    /// </summary>
    IReadOnlySet<string> GetConditionallyOptionalQuestionIds(JourneyPage page, JourneyConditionContext ctx);

    /// <summary>
    /// Whether the page's <see cref="JourneyPage.RequireAtLeastOne"/> rule applies to this
    /// context. False when the page does not set the flag. When it also sets
    /// <see cref="JourneyPage.RequireAtLeastOneWhen"/>, the rule applies only while every
    /// named condition is registered and true — an unregistered name keeps the rule on
    /// (fail closed), the same way OptionalWhen keeps a question mandatory.
    /// </summary>
    bool IsRequireAtLeastOneActive(JourneyPage page, JourneyConditionContext ctx);
}
