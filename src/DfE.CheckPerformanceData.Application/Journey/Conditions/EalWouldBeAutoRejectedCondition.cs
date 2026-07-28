namespace DfE.CheckPerformanceData.Application.Journey.Conditions;

/// <summary>
/// True when an "Admitted from abroad with English not first language" removal would
/// be auto-rejected by the rules engine's EAL-REJ-ENG / EAL-REJ-OTH-ENGCOUNTRY rules,
/// approximated journey-side so the evidence page can relax its mandatory fields
/// (PBI 292266). Mirrors the engine exactly: only the certain answers english / other
/// can waive evidence. believed-english / believed-other map to Uncertain in the
/// engine, never fire a reject rule, and go to Scrutiny — a reviewer will read those
/// requests, so evidence stays mandatory (AC Scenario 004 withdrawn, BA decision
/// 2026-07-28). chose-not-to-say / not-known and an unresolved country also fail safe
/// to false (evidence stays mandatory).
/// </summary>
public sealed class EalWouldBeAutoRejectedCondition : IJourneyCondition
{
    private const string ReasonQuestionId = "reason";
    private const string EalReasonValue = "english-not-first-language";
    private const string FirstLanguageQuestionId = "first-language";
    private const string English = "English";
    private const string EnglishFirstLanguage = "english";
    private const string OtherFirstLanguage = "other";

    public string Name => "EalWouldBeAutoRejected";

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

        if (string.Equals(language, EnglishFirstLanguage, StringComparison.Ordinal))
            return true;

        return string.Equals(language, OtherFirstLanguage, StringComparison.Ordinal)
            && ctx.Journey.OriginCountryLanguages?
                .Any(l => string.Equals(l, English, StringComparison.OrdinalIgnoreCase)) == true;
    }
}
