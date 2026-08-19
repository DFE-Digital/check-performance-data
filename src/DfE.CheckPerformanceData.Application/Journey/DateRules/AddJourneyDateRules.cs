namespace DfE.CheckPerformanceData.Application.Journey.DateRules;

/// <summary>
/// Date rules for the Add-a-pupil journey (AB#297310). Two dates must not be later than
/// today: the pupil's date of birth (learner-details) and the admission date (admission-details)
/// — a future date is rejected with a "must be in the past" message, while today and any past
/// date are acceptable.
///
/// These live in code rather than in the flow JSON on purpose, for the same reason as
/// <see cref="RemovalJourneyDateRules"/>: flow configs are served from blob at runtime and only
/// reach blob via an environment-gated seeding step, so a JSON-declared rule can be silently
/// absent in a deployed environment. A rule compiled into the container cannot go missing.
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
    /// Returns one <see cref="DateFieldViolation"/> per Date question on the page whose parsed
    /// date is strictly later than <paramref name="today"/>, in page question order. Today itself
    /// is acceptable. A null or unparseable date (blank, part-filled, or an impossible calendar
    /// date) is ignored — the per-question format rules own that failure and only the first error
    /// per question is ever rendered.
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
