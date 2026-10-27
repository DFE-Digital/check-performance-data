namespace DfE.CheckPerformanceData.Application.Journey.Conditions;

/// <summary>
/// True when the shared KS4 June or KS2 evidence page should be optional for an
/// "Admitted from abroad with English not first language" removal (AB#309468).
/// Evidence is optional for exactly four first-language answers — other,
/// believed-other, chose-not-to-say and not-known — regardless of the origin
/// country. English and believed-English keep mandatory evidence.
/// </summary>
public sealed class EalEvidenceIsOptionalCondition : IJourneyCondition
{
    private const string ReasonQuestionId = "reason";
    private const string EalReasonValue = "english-not-first-language";
    private const string FirstLanguageQuestionId = "first-language";

    private static readonly HashSet<string> OptionalFirstLanguages = new(StringComparer.Ordinal)
    {
        "other",
        "believed-other",
        "chose-not-to-say",
        "not-known"
    };

    public string Name => "EalEvidenceIsOptional";

    public bool Evaluate(JourneyConditionContext ctx)
    {
        var answers = ctx.Journey.QuestionAnswers;

        // The evidence page is shared across removal branches: only the EAL reason
        // may relax it.
        if (!answers.TryGetValue(ReasonQuestionId, out var reason)
            || !string.Equals(reason.TextValue, EalReasonValue, StringComparison.Ordinal))
            return false;

        if (!answers.TryGetValue(FirstLanguageQuestionId, out var firstLanguage)
            || firstLanguage.TextValue is not { } language)
            return false;

        return OptionalFirstLanguages.Contains(language);
    }
}
