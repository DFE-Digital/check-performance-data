namespace DfE.CheckPerformanceData.Application.Journey.Conditions;

/// <summary>
/// True when an "Admitted from abroad with English not first language" removal would
/// be auto-rejected by the rules engine's EAL-REJ-ENG / EAL-REJ-OTH-ENGCOUNTRY rules,
/// approximated journey-side so the evidence page can relax its mandatory fields
/// (PBI 292266). Deliberate divergence from the engine: believed-english /
/// believed-other count as their certain counterparts here (per the AC), although the
/// engine maps them to Uncertain and defers those requests to Scrutiny.
/// chose-not-to-say / not-known and an unresolved country fail safe to false
/// (evidence stays mandatory).
/// </summary>
public sealed class EalWouldBeAutoRejectedCondition : IJourneyCondition
{
    private const string ReasonQuestionId = "reason";
    private const string EalReasonValue = "english-not-first-language";
    private const string FirstLanguageQuestionId = "first-language";
    private const string English = "English";

    private static readonly string[] EnglishFirstLanguage = ["english", "believed-english"];
    private static readonly string[] OtherFirstLanguage = ["other", "believed-other"];

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

        if (EnglishFirstLanguage.Contains(language, StringComparer.Ordinal))
            return true;

        return OtherFirstLanguage.Contains(language, StringComparer.Ordinal)
            && ctx.Journey.OriginCountryLanguages?
                .Any(l => string.Equals(l, English, StringComparison.OrdinalIgnoreCase)) == true;
    }
}
