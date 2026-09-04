namespace DfE.CheckPerformanceData.Application.Journey.DateRules;

/// <summary>
/// Date rules for the eight "Remove" journeys: the six KS4 ones (admitted following
/// permanent exclusion, child missing education, pupil has died, elective home
/// education, permanently excluded from current school, permanently left England)
/// and the two 16-19 ones (student has died, not at end of 16-19 study — AB#298403).
/// The single removal/exclusion date on each page must be a real calendar date no
/// later than today — a future date is rejected with the journey-specific
/// "Date … must be in the past" message. Today itself is accepted on every journey:
/// a pupil removed from the roll today is a legitimate answer.
///
/// Wording follows the page, not the question id. Four KS4 pages and both 16-19 pages
/// share the <see cref="DateRemovedFromRoll"/> question, but the 16-19 student-died
/// page asks about the "school or college roll" — see
/// <c>PageSpecificFutureDateTemplates</c>.
///
/// These live in code rather than in the flow JSON on purpose, for the same reason
/// as <see cref="PageDateRules"/>: a rule expressed as data is a rule that can be
/// edited, partially applied or dropped without a compiler noticing, whereas one
/// compiled into the container is exactly as deployed as the journey it guards.
/// (The original reason was stronger still — configs then reached a deployed
/// environment only via a Development-gated blob seeding step, so a JSON-declared
/// rule could be silently absent. Configs now ship in the image, so that specific
/// failure is gone; see docs/question-flow-deployment.md.)
/// QuestionFlowValidatorAlignmentTests pins the ids below to the shipped config so
/// the two cannot drift apart unnoticed.
///
/// The english-not-first-language-details page (AB#295246) is deliberately NOT in
/// this class — it has its own cross-field rules in <see cref="PageDateRules"/>.
///
/// The page-id set spans two flow configs (Remove_KS4June and Remove_Post16). Page ids
/// are unique across every shipped config, which QuestionFlowValidatorAlignmentTests
/// relies on when it resolves each id to a single page.
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

    /// <summary>AB#298403: the 16-19 "student has died" page (Remove_Post16).</summary>
    public const string StudentDiedPageId = "student-died";

    /// <summary>AB#298403: the 16-19 "not at end of 16-19 study" page (Remove_Post16).</summary>
    public const string NotAtEndOfStudyPageId = "not-at-end-of-16-19-study";

    public static readonly IReadOnlySet<string> RemovalPageIds = new HashSet<string>(
        [
            PermanentExclusionPageId,
            ChildMissingEducationPageId,
            PupilDiedPageId,
            ElectiveHomeEducationPageId,
            PermanentlyExcludedPageId,
            PermanentlyLeftEnglandPageId,
            StudentDiedPageId,
            NotAtEndOfStudyPageId
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

    /// <summary>
    /// AB#298403: the 16-19 student-died page asks about the "school or college roll", so its
    /// error must say the same. It shares the <see cref="DateRemovedFromRoll"/> question id with
    /// four KS4 pages, so the wording cannot be keyed by question id alone.
    /// </summary>
    private const string DateRemovedFromSchoolOrCollegeRollInFuture =
        "Date {pupilName} was removed from your school or college roll must be in the past";

    private static readonly IReadOnlyDictionary<string, string> FutureDateTemplates =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [DatePupilExcluded] = DatePupilExcludedInFuture,
            [DatePermanentlyExcluded] = DatePermanentlyExcludedInFuture,
            [DateRemovedFromRoll] = DateRemovedFromRollInFuture
        };

    /// <summary>
    /// Per-page wording overrides, keyed by "{pageId}\u0000{questionId}". A page appears here only
    /// when its question asks something the shared, question-id-keyed template words differently —
    /// the message must mirror the question the user is reading. The 16-19
    /// <see cref="NotAtEndOfStudyPageId"/> page is deliberately absent: it asks about the "school
    /// roll", so the shared wording is already correct for it.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> PageSpecificFutureDateTemplates =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [TemplateKey(StudentDiedPageId, DateRemovedFromRoll)] = DateRemovedFromSchoolOrCollegeRollInFuture
        };

    private static string TemplateKey(string pageId, string questionId) => $"{pageId}\u0000{questionId}";

    /// <summary>
    /// True when <paramref name="pageId"/> is one of the eight in-scope removal pages,
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
            // The page-specific wording wins where one exists; otherwise the question's own
            // shared template. No template at all means the question is out of scope.
            if (!PageSpecificFutureDateTemplates.TryGetValue(TemplateKey(page.Id, question.Id), out var template)
                && !FutureDateTemplates.TryGetValue(question.Id, out template)) continue;

            answers.TryGetValue(question.Id, out var answer);
            var date = answer?.DateValue?.ToDateOnly();
            if (date is null || date <= today) continue;

            violations.Add(new DateFieldViolation(question.Id, JourneyTemplate.Resolve(template, pupilName)));
        }

        return violations;
    }
}
