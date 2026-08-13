namespace DfE.CheckPerformanceData.Application.Journey.DateRules;

/// <summary>
/// Date rules for the six KS4 "Remove" journeys (admitted following permanent
/// exclusion, child missing education, pupil has died, elective home education,
/// permanently excluded from current school, permanently left England). The single
/// removal/exclusion date on each page must be a real calendar date no later than
/// today — a future date is rejected with the journey-specific
/// "Date … must be in the past" message.
///
/// These live in code rather than in the flow JSON on purpose, for the same reason
/// as <see cref="PageDateRules"/>: flow configs are served from blob at runtime and
/// only reach blob via an environment-gated seeding step, so a JSON-declared rule
/// can be silently absent in a deployed environment. A rule compiled into the
/// container cannot go missing. QuestionFlowValidatorAlignmentTests pins the ids
/// below to the shipped config so the two cannot drift apart unnoticed.
///
/// The english-not-first-language-details page (AB#295246) is deliberately NOT in
/// this class — it has its own cross-field rules in <see cref="PageDateRules"/>.
/// </summary>
public static class RemovalJourneyDateRules
{
    // ── Page ids (all six in-scope removal journeys) ─────────────────────────

    /// <summary>Scenario 001: admitted following permanent exclusion.</summary>
    public const string PermanentExclusionPageId = "permanent-exclusion";

    /// <summary>Scenario 002: child missing education.</summary>
    public const string ChildMissingEducationPageId = "child-missing-education";

    /// <summary>Scenario 003: pupil has died.</summary>
    public const string PupilDiedPageId = "pupil-died";

    /// <summary>Scenario 004: elective home education.</summary>
    public const string ElectiveHomeEducationPageId = "elective-home-education";

    /// <summary>Scenario 005: permanently excluded from current school.</summary>
    public const string PermanentlyExcludedPageId = "permanently-excluded";

    /// <summary>Scenario 006: permanently left England.</summary>
    public const string PermanentlyLeftEnglandPageId = "permanently-left-england-questions";

    public static readonly IReadOnlySet<string> RemovalPageIds = new HashSet<string>(
        [
            PermanentExclusionPageId,
            ChildMissingEducationPageId,
            PupilDiedPageId,
            ElectiveHomeEducationPageId,
            PermanentlyExcludedPageId,
            PermanentlyLeftEnglandPageId
        ],
        StringComparer.Ordinal);

    // ── Date question ids ────────────────────────────────────────────────────

    /// <summary>The exclusion date on scenario 001 (page permanent-exclusion).</summary>
    public const string DatePupilExcluded = "date-pupil-excluded";

    /// <summary>The exclusion date on scenario 005 (page permanently-excluded).</summary>
    public const string DatePermanentlyExcluded = "date-permanently-excluded";

    /// <summary>The roll-removal date shared by scenarios 002, 003, 004 and 006.</summary>
    public const string DateRemovedFromRoll = "date-removed-from-roll";

    /// <summary>
    /// The three removal date question ids. Used to scope the invalid-date message
    /// fallback in <see cref="JourneyValidationService.ValidateAnswer"/> to these
    /// questions only, so the excluded EAL page keeps its own wording.
    /// </summary>
    public static readonly IReadOnlySet<string> RemovalDateQuestionIds = new HashSet<string>(
        [DatePupilExcluded, DatePermanentlyExcluded, DateRemovedFromRoll],
        StringComparer.Ordinal);

    // ── Future-date message templates (exact spec wording, no leading "The") ──

    private const string DatePupilExcludedInFuture =
        "Date {pupilName} was excluded must be in the past";

    private const string DatePermanentlyExcludedInFuture =
        "Date {pupilName} was permanently excluded from your school must be in the past";

    private const string DateRemovedFromRollInFuture =
        "Date {pupilName} was removed from your school roll must be in the past";

    private static readonly IReadOnlyDictionary<string, string> FutureDateTemplates =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [DatePupilExcluded] = DatePupilExcludedInFuture,
            [DatePermanentlyExcluded] = DatePermanentlyExcludedInFuture,
            [DateRemovedFromRoll] = DateRemovedFromRollInFuture
        };

    /// <summary>
    /// True when <paramref name="pageId"/> is one of the six in-scope removal pages,
    /// i.e. its date questions should be checked for future dates. Used by
    /// <see cref="JourneyValidationService.ValidatePageDates"/> to dispatch.
    /// </summary>
    public static bool AppliesToPage(string pageId) =>
        RemovalPageIds.Contains(pageId);

    /// <summary>
    /// Returns one <see cref="DateFieldViolation"/> per Date question on the page whose
    /// parsed date is strictly later than <paramref name="today"/>, in page question
    /// order. Today itself is acceptable. A null or unparseable date (blank, part-filled,
    /// or an impossible calendar date) is ignored — the per-question format rules own
    /// that failure and only the first error per question is ever rendered.
    /// </summary>
    public static IReadOnlyList<DateFieldViolation> EvaluateFutureDates(
        JourneyPage page, IReadOnlyDictionary<string, QuestionAnswer> answers, DateOnly today, string pupilName)
    {
        var violations = new List<DateFieldViolation>();

        foreach (var question in page.Questions)
        {
            if (question.Type != QuestionType.Date) continue;
            if (!FutureDateTemplates.TryGetValue(question.Id, out var template)) continue;

            answers.TryGetValue(question.Id, out var answer);
            var date = answer?.DateValue?.ToDateOnly();
            if (date is null || date <= today) continue;

            violations.Add(new DateFieldViolation(question.Id, JourneyTemplate.Resolve(template, pupilName)));
        }

        return violations;
    }
}
