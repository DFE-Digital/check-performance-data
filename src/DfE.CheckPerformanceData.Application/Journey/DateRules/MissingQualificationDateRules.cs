namespace DfE.CheckPerformanceData.Application.Journey.DateRules;

/// <summary>
/// Award-date rules for the missing-qualification enquiry (AB#298201): the date must be no later
/// than UK-today and no earlier than 1 September 2023 — the start of the 2023/24 academic year,
/// the oldest year enquiries cover. In code rather than flow JSON for the same reason as
/// <see cref="RemovalJourneyDateRules"/>: a JSON-declared rule can be silently absent in a
/// deployed environment; a compiled rule cannot.
/// </summary>
public static class MissingQualificationDateRules
{
    public const string AwardDatePageId = "qualification-details";
    public const string AwardDateQuestionId = "q-award-date";

    /// <summary>
    /// Start of the oldest academic year enquiries cover. The rejection copy names 2023/24 and
    /// 2024/25 explicitly, so when this date moves the message below must move with it — both are
    /// pinned by MissingQualificationDateRulesTests.
    /// </summary>
    public static readonly DateOnly EarliestAwardDate = new(2023, 9, 1);

    private const string FutureMessage = "Award date must be today or in the past";
    private const string OutsideWindowMessage =
        "We are only able to allow results enquiries for results awarded during the 2023/24 and 2024/25 academic years";

    public static bool AppliesToPage(string pageId) =>
        string.Equals(pageId, AwardDatePageId, StringComparison.Ordinal);

    /// <summary>
    /// At most one violation: future beats too-old (a future date is also outside the window, and
    /// two messages on one field render as noise). Null/unparseable dates are the format rules'
    /// concern and produce nothing here.
    /// </summary>
    public static IReadOnlyList<DateFieldViolation> Evaluate(
        JourneyPage page, IReadOnlyDictionary<string, QuestionAnswer> answers, DateOnly today)
    {
        answers.TryGetValue(AwardDateQuestionId, out var answer);
        var date = answer?.DateValue?.ToDateOnly();
        if (date is null) return [];
        if (date > today) return [new DateFieldViolation(AwardDateQuestionId, FutureMessage)];
        if (date < EarliestAwardDate) return [new DateFieldViolation(AwardDateQuestionId, OutsideWindowMessage)];
        return [];
    }
}
