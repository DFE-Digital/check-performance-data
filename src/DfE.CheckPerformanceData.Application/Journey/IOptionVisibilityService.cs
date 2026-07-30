namespace DfE.CheckPerformanceData.Application.Journey;

public interface IOptionVisibilityService
{
    /// <summary>
    /// Returns the options of <paramref name="question"/> that should be shown
    /// for the given context, preserving input order. Options with no
    /// VisibleWhen are always shown; a VisibleWhen naming an unregistered
    /// condition is hidden (fail closed). Never mutates the question.
    /// <para>
    /// Rendering and POST validation both consume this filter: the controller
    /// rejects posted radio values that are not in the visible set, so hidden
    /// options cannot be selected by hand-crafting a request.
    /// </para>
    /// </summary>
    IReadOnlyList<QuestionOption> GetVisibleOptions(Question question, JourneyConditionContext ctx);
}
