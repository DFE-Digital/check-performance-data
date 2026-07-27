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
}
