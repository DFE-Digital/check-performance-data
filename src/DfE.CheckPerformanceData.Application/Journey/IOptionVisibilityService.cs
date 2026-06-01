namespace DfE.CheckPerformanceData.Application.Journey;

public interface IOptionVisibilityService
{
    /// <summary>
    /// Returns the options of <paramref name="question"/> that should be shown
    /// for the given context, preserving input order. Options with no
    /// VisibleWhen are always shown; a VisibleWhen naming an unregistered
    /// condition is hidden (fail closed). Never mutates the question.
    /// <para>
    /// This is a render-only filter: the controller does not reject posted radio values
    /// that belong to hidden options. Submissions are reviewed by Zendesk staff, which
    /// serves as the downstream enforcement point.
    /// </para>
    /// </summary>
    IReadOnlyList<QuestionOption> GetVisibleOptions(Question question, JourneyConditionContext ctx);
}
