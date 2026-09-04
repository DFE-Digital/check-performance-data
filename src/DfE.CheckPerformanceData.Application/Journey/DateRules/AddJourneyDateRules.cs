namespace DfE.CheckPerformanceData.Application.Journey.DateRules;

/// <summary>
/// Date rules for the Add-a-pupil journey (AB#297310). Two dates must not be later than
/// today: the pupil's date of birth (learner-details) and the admission date (admission-details)
/// — a future date is rejected with a "must be in the past" message, while today and any past
/// date are acceptable. The two must also be in order: a pupil cannot be admitted before they
/// were born.
///
/// These live in code rather than in the flow JSON on purpose, for the same reason as
/// <see cref="RemovalJourneyDateRules"/>: a rule compiled into the container is exactly as
/// deployed as the journey it guards, where one expressed as data can be edited or dropped with
/// nothing to indicate validation stopped happening.
/// QuestionFlowValidatorAlignmentTests pins the ids below to the shipped config so the two
/// cannot drift apart unnoticed.
///
/// The page ids here are duplicated from (and must equal) <c>AddPupilJourney.LearnerDetailsPageId</c>
/// / <c>AddPupilJourney.AdmissionDetailsPageId</c> in the Web layer — Application cannot reference
/// Web, so AddFlowTests pins both copies to the same shipped JSON.
/// </summary>
public static class AddJourneyDateRules
{
    /// <summary>Must equal AddPupilJourney.LearnerDetailsPageId.</summary>
    public const string LearnerDetailsPageId = "learner-details";

    /// <summary>Must equal AddPupilJourney.AdmissionDetailsPageId.</summary>
    public const string AdmissionDetailsPageId = "admission-details";

    public const string DateOfBirth = "date-of-birth";
    public const string AdmissionDate = "admission-date";

    private const string DateOfBirthInFuture = "Date of birth must be in the past";

    private const string AdmissionDateInFuture =
        "Date {pupilName} was admitted to your school must be in the past";

    // The two dates sit on different pages, so each side of the ordering rule needs its own
    // wording: the user only ever sees the one anchored to the page they are on.
    private const string AdmissionBeforeDateOfBirth =
        "Date {pupilName} was admitted to your school must be the same as or after their date of birth";

    private const string DateOfBirthAfterAdmission =
        "Date of birth must be the same as or before the date {pupilName} was admitted to your school";

    private static readonly IReadOnlyDictionary<string, string> FutureDateTemplates =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [DateOfBirth] = DateOfBirthInFuture,
            [AdmissionDate] = AdmissionDateInFuture
        };

    /// <summary>
    /// True when <paramref name="pageId"/> is one of the two in-scope Add pages, i.e. its date
    /// questions should be checked for future dates. Used by
    /// <see cref="JourneyValidationService.ValidatePageDates"/> to dispatch.
    /// </summary>
    public static bool AppliesToPage(string pageId) =>
        pageId is LearnerDetailsPageId or AdmissionDetailsPageId;

    /// <summary>
    /// Applies both rules to the page and returns at most one message per question, in page
    /// question order.
    ///
    /// A null or unparseable date (blank, part-filled, or an impossible calendar date) is ignored
    /// — the per-question format rules own that failure and only the first error per question is
    /// ever rendered.
    ///
    /// <paramref name="answers"/> must span the whole journey, not just the page: the ordering
    /// rule compares a date the user is entering now against one they entered on the other page.
    ///
    /// The future-date check runs first. A date that is independently wrong makes any comparison
    /// against it misleading, so it should be the message the user sees.
    /// </summary>
    public static IReadOnlyList<DateFieldViolation> Evaluate(
        JourneyPage page, IReadOnlyDictionary<string, QuestionAnswer> answers, DateOnly today, string pupilName)
    {
        var messages = new Dictionary<string, string>(StringComparer.Ordinal);

        void Flag(string questionId, string template)
        {
            if (messages.ContainsKey(questionId)) return;
            messages[questionId] = JourneyTemplate.Resolve(template, pupilName);
        }

        DateOnly? Answered(string questionId) =>
            answers.TryGetValue(questionId, out var answer) ? answer.DateValue?.ToDateOnly() : null;

        foreach (var question in page.Questions)
        {
            if (question.Type != QuestionType.Date) continue;
            if (!FutureDateTemplates.TryGetValue(question.Id, out var template)) continue;
            // Today itself is acceptable — the rule is "later than today", not "before today".
            if (Answered(question.Id) is { } date && date > today) Flag(question.Id, template);
        }

        // Lifted nullable ordering: with either side null the operator yields false, so a missing
        // or unusable date drops out of the rule rather than needing a guard. Equal dates pass —
        // a pupil admitted on their date of birth is implausible, not invalid, and this rule is
        // here to stop the impossible reaching the LDS egress.
        if (Answered(AdmissionDate) < Answered(DateOfBirth))
        {
            // Anchored to whichever of the two questions is on the page being validated, so the
            // error renders against a field the user can actually see and correct. Editing the
            // date of birth from the summary reaches this rule via learner-details.
            if (HasQuestion(page, DateOfBirth)) Flag(DateOfBirth, DateOfBirthAfterAdmission);
            if (HasQuestion(page, AdmissionDate)) Flag(AdmissionDate, AdmissionBeforeDateOfBirth);
        }

        return page.Questions
            .Where(q => messages.ContainsKey(q.Id))
            .Select(q => new DateFieldViolation(q.Id, messages[q.Id]))
            .ToList();
    }

    private static bool HasQuestion(JourneyPage page, string questionId) =>
        page.Questions.Any(q => string.Equals(q.Id, questionId, StringComparison.Ordinal));
}
