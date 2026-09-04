using System.Globalization;

namespace DfE.CheckPerformanceData.Application.Journey.DateRules;

/// <summary>A cross-field date rule failure, anchored to the question that should be corrected.</summary>
/// <param name="QuestionId">The raw question id from the flow config — this becomes the ModelState
/// key, which is what <c>JourneyViewModelBuilder</c> looks the error up by.</param>
public sealed record DateFieldViolation(string QuestionId, string Message);

/// <summary>
/// Cross-field date rules for the "Admitted from abroad with English not first language" page
/// (AB#295246). The three dates must satisfy
/// <c>arrivedInEngland &lt;= firstEnglishSchool &lt;= startedAtSchool</c>, none may be in the
/// future, and the school start date may not predate the cohort.
///
/// These live in code rather than in the flow JSON on purpose: a rule expressed as data can be
/// edited, partially applied or dropped with nothing to indicate validation stopped happening,
/// whereas one compiled into the container is exactly as deployed as the journey it guards.
/// (The original reason was stronger still — configs then reached a deployed environment only via
/// a Development-gated blob seeding step, so a JSON-declared rule could be silently absent. They
/// now ship in the image; see docs/question-flow-deployment.md.)
/// QuestionFlowValidatorAlignmentTests pins the ids below to the shipped config so the two cannot
/// drift apart unnoticed.
/// </summary>
public static class PageDateRules
{
    public const string EalDetailsPageId = "english-not-first-language-details";

    /// <summary>a. When did {pupilName} start at your school? (required)</summary>
    public const string StartedAtSchool = "date-pupil-started";

    /// <summary>b. When did {pupilName} first start at a school in England? (required)</summary>
    public const string FirstEnglishSchool = "date-pupil-started-school-in-england";

    /// <summary>c. When did {pupilName} arrive in England? (optional)</summary>
    public const string ArrivedInEngland = "date-pupil-arrived-in-england";

    // Scenario 001 (a < b)
    private const string StartedBeforeFirstEnglishSchool =
        "The date {pupilName} started at your school must be the same as or after {date} " +
        "when they first started at a school in England";

    private const string FirstEnglishSchoolAfterStarted =
        "The date {pupilName} first started at a school in England must be the same as or before " +
        "{date} when they started at your school";

    // Scenario 002 (a < c)
    private const string StartedBeforeArrival =
        "The date {pupilName} started at your school must be the same as or after {date} " +
        "when they arrived in England";

    private const string ArrivedAfterStarted =
        "The date {pupilName} arrived in England must be the same as or before {date} " +
        "when they started at your school";

    // Scenario 003 (b < c)
    private const string FirstEnglishSchoolBeforeArrival =
        "The date {pupilName} first started at a school in England must be the same as or after " +
        "{date} when they arrived in England";

    private const string ArrivedAfterFirstEnglishSchool =
        "The date {pupilName} arrived in England must be the same as or before {date} " +
        "when they first started at a school in England";

    // Scenarios 004-006 (in the future)
    private const string StartedInFuture =
        "The date {pupilName} started at your school must be in the past";

    private const string FirstEnglishSchoolInFuture =
        "The date {pupilName} first started at a school in England must be in the past";

    private const string ArrivedInFuture =
        "The date {pupilName} arrived in England must be in the past";

    // Scenario 007 (a predates the cohort)
    private const string StartedBeforeCohort =
        "The date {pupilName} started at your school cannot be more than 4 years ago " +
        "when the cohort began";

    /// <summary>
    /// Earliest acceptable school admission date: 1 September of the school year four years back.
    /// Mirrors the boundary the rules engine already applies to the same field —
    /// <c>RulesEngineWorker/seed/rules.json</c> carries
    /// <c>{ "field": "schoolAdmissionDate", "gte": "2022-09-01" }</c>, which is this expression
    /// evaluated for a 2026 checking window. If the engine's boundary is ever re-cut, this must
    /// move with it; PageDateRulesTests pins the 2026 case so the pair cannot drift silently.
    /// </summary>
    public static DateOnly CohortStart(DateOnly today) => new(today.Year - 4, 9, 1);

    /// <summary>
    /// Applies every scenario to the three dates and returns at most one message per field.
    ///
    /// A null date is one the user left blank, part-filled, or entered as a non-date; it is
    /// excluded from every comparison it takes part in, because the per-question format rules
    /// already own that failure and only the first error per question is ever rendered.
    ///
    /// Ordering matters. The "in the future" and "predates the cohort" checks run before the
    /// three sequence checks: a date that is independently wrong makes any comparison against
    /// it misleading, so it should be the message the user sees.
    /// </summary>
    public static IReadOnlyList<DateFieldViolation> Evaluate(
        DateOnly? startedAtSchool,
        DateOnly? firstEnglishSchool,
        DateOnly? arrivedInEngland,
        DateOnly today,
        string pupilName)
    {
        var messages = new Dictionary<string, string>(StringComparer.Ordinal);

        void Flag(string questionId, string template, DateOnly? reference = null)
        {
            if (messages.ContainsKey(questionId)) return;
            messages[questionId] = Format(template, pupilName, reference);
        }

        // Every comparison below relies on lifted nullable ordering: when either side is null the
        // operator yields false, so a missing or unusable date silently drops out of the rule
        // rather than needing a guard at each site.

        // 004-006: no date may be in the future. Today itself is acceptable — the rule is
        // "later than today", not "before today".
        if (startedAtSchool > today) Flag(StartedAtSchool, StartedInFuture);
        if (firstEnglishSchool > today) Flag(FirstEnglishSchool, FirstEnglishSchoolInFuture);
        if (arrivedInEngland > today) Flag(ArrivedInEngland, ArrivedInFuture);

        // 007: the school start date may not predate the cohort. Field a. only.
        if (startedAtSchool < CohortStart(today)) Flag(StartedAtSchool, StartedBeforeCohort);

        // 001-003: arrival <= first English school <= started at this school. Each failure is
        // reported against both fields involved, phrased from that field's point of view.
        if (startedAtSchool < firstEnglishSchool)
        {
            Flag(StartedAtSchool, StartedBeforeFirstEnglishSchool, firstEnglishSchool);
            Flag(FirstEnglishSchool, FirstEnglishSchoolAfterStarted, startedAtSchool);
        }

        if (startedAtSchool < arrivedInEngland)
        {
            Flag(StartedAtSchool, StartedBeforeArrival, arrivedInEngland);
            Flag(ArrivedInEngland, ArrivedAfterStarted, startedAtSchool);
        }

        if (firstEnglishSchool < arrivedInEngland)
        {
            Flag(FirstEnglishSchool, FirstEnglishSchoolBeforeArrival, arrivedInEngland);
            Flag(ArrivedInEngland, ArrivedAfterFirstEnglishSchool, firstEnglishSchool);
        }

        // Returned in the order the questions appear on the page so the error summary reads
        // top-to-bottom. (The summary is built from the page's question order regardless, but
        // callers and tests get a stable order too.)
        return OrderedIds
            .Where(messages.ContainsKey)
            .Select(id => new DateFieldViolation(id, messages[id]))
            .ToList();
    }

    private static readonly string[] OrderedIds = [StartedAtSchool, FirstEnglishSchool, ArrivedInEngland];

    // Dates are rendered "d MMMM yyyy" (e.g. "27 March 2026") to match how the same answers
    // appear on the summary page. Invariant culture: the message must not change shape with the
    // server's ambient culture.
    private static string Format(string template, string pupilName, DateOnly? reference) =>
        JourneyTemplate.Resolve(template, pupilName)
            .Replace(
                "{date}",
                reference?.ToString("d MMMM yyyy", CultureInfo.InvariantCulture) ?? string.Empty,
                StringComparison.Ordinal);
}
