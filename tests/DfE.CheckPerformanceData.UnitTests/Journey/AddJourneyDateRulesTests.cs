using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.Journey.DateRules;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

/// <summary>
/// The Add-a-pupil journey (AB#297310). Two dates — date of birth (learner-details) and
/// admission date (admission-details) — must not be later than today, and must be in order:
/// a pupil cannot be admitted before they were born. A future date is rejected with a "must be
/// in the past" message; today and any past date are acceptable. A blank, part-filled or
/// impossible date is not this rule's to report: the per-question format rules own that failure.
/// </summary>
public sealed class AddJourneyDateRulesTests
{
    private static readonly DateOnly Today = new(2026, 6, 15);

    private const string Pupil = "Alice Smith";

    [Fact]
    public void FutureDateOfBirth_IsRejected_WithTheExactMessage()
    {
        var violation = Assert.Single(Evaluate(
            AddJourneyDateRules.LearnerDetailsPageId,
            AddJourneyDateRules.DateOfBirth,
            Today.AddDays(1)));

        Assert.Equal(AddJourneyDateRules.DateOfBirth, violation.QuestionId);
        Assert.Equal("Date of birth must be in the past", violation.Message);
    }

    [Fact]
    public void FutureAdmissionDate_IsRejected_WithThePupilNameResolved()
    {
        var violation = Assert.Single(Evaluate(
            AddJourneyDateRules.AdmissionDetailsPageId,
            AddJourneyDateRules.AdmissionDate,
            Today.AddDays(1)));

        Assert.Equal(AddJourneyDateRules.AdmissionDate, violation.QuestionId);
        Assert.Equal("Date Alice Smith was admitted to your school must be in the past", violation.Message);
    }

    [Theory]
    [InlineData(nameof(AddJourneyDateRules.LearnerDetailsPageId), nameof(AddJourneyDateRules.DateOfBirth))]
    [InlineData(nameof(AddJourneyDateRules.AdmissionDetailsPageId), nameof(AddJourneyDateRules.AdmissionDate))]
    public void Today_IsAcceptable(string pageIdField, string questionIdField)
    {
        var pageId = (string)typeof(AddJourneyDateRules).GetField(pageIdField)!.GetValue(null)!;
        var questionId = (string)typeof(AddJourneyDateRules).GetField(questionIdField)!.GetValue(null)!;

        Assert.Empty(Evaluate(pageId, questionId, Today));
    }

    [Theory]
    [InlineData(nameof(AddJourneyDateRules.LearnerDetailsPageId), nameof(AddJourneyDateRules.DateOfBirth))]
    [InlineData(nameof(AddJourneyDateRules.AdmissionDetailsPageId), nameof(AddJourneyDateRules.AdmissionDate))]
    public void PastDate_IsAcceptable(string pageIdField, string questionIdField)
    {
        var pageId = (string)typeof(AddJourneyDateRules).GetField(pageIdField)!.GetValue(null)!;
        var questionId = (string)typeof(AddJourneyDateRules).GetField(questionIdField)!.GetValue(null)!;

        Assert.Empty(Evaluate(pageId, questionId, Today.AddDays(-365)));
    }

    [Fact]
    public void BlankDate_ProducesNoViolation()
    {
        Assert.Empty(Evaluate(
            AddJourneyDateRules.LearnerDetailsPageId,
            AddJourneyDateRules.DateOfBirth,
            date: null));
    }

    [Fact]
    public void IncompleteDate_ProducesNoViolation()
    {
        var violations = EvaluateAnswer(
            AddJourneyDateRules.LearnerDetailsPageId,
            AddJourneyDateRules.DateOfBirth,
            new DateAnswer { Day = 0, Month = 0, Year = 0 });

        Assert.Empty(violations);
    }

    [Fact]
    public void ImpossibleCalendarDate_ProducesNoViolation()
    {
        var violations = EvaluateAnswer(
            AddJourneyDateRules.LearnerDetailsPageId,
            AddJourneyDateRules.DateOfBirth,
            new DateAnswer { Day = 31, Month = 2, Year = 2026 });

        Assert.Empty(violations);
    }

    [Theory]
    [InlineData(AddJourneyDateRules.LearnerDetailsPageId, true)]
    [InlineData(AddJourneyDateRules.AdmissionDetailsPageId, true)]
    [InlineData("evidence", false)]
    [InlineData("summary", false)]
    public void AppliesToPage_IsTrueOnlyForTheTwoAddPages(string pageId, bool expected) =>
        Assert.Equal(expected, AddJourneyDateRules.AppliesToPage(pageId));

    // ── Admission date must not precede date of birth ───────────────────────
    //
    // The two dates are on different pages, so the rule reads both from the journey's answers
    // and anchors the error to whichever question the page being validated actually shows —
    // otherwise the user is told to correct a field that is not in front of them.

    [Fact]
    public void AdmissionBeforeDateOfBirth_OnAdmissionDetails_IsAnchoredToTheAdmissionDate()
    {
        var violation = Assert.Single(EvaluateOrdering(
            AddJourneyDateRules.AdmissionDetailsPageId,
            AddJourneyDateRules.AdmissionDate,
            dateOfBirth: new DateOnly(2025, 9, 1),
            admissionDate: new DateOnly(2010, 9, 1)));

        Assert.Equal(AddJourneyDateRules.AdmissionDate, violation.QuestionId);
        Assert.Equal(
            "Date Alice Smith was admitted to your school must be the same as or after their date of birth",
            violation.Message);
    }

    // Reached by editing the date of birth from the summary once an admission date is recorded.
    [Fact]
    public void AdmissionBeforeDateOfBirth_OnLearnerDetails_IsAnchoredToTheDateOfBirth()
    {
        var violation = Assert.Single(EvaluateOrdering(
            AddJourneyDateRules.LearnerDetailsPageId,
            AddJourneyDateRules.DateOfBirth,
            dateOfBirth: new DateOnly(2025, 9, 1),
            admissionDate: new DateOnly(2010, 9, 1)));

        Assert.Equal(AddJourneyDateRules.DateOfBirth, violation.QuestionId);
        Assert.Equal(
            "Date of birth must be the same as or before the date Alice Smith was admitted to your school",
            violation.Message);
    }

    [Fact]
    public void AdmissionAfterDateOfBirth_IsAcceptable()
    {
        Assert.Empty(EvaluateOrdering(
            AddJourneyDateRules.AdmissionDetailsPageId,
            AddJourneyDateRules.AdmissionDate,
            dateOfBirth: new DateOnly(2010, 9, 1),
            admissionDate: new DateOnly(2015, 9, 1)));
    }

    // Implausible, not impossible — the rule exists to stop what cannot be true reaching the
    // LDS egress, and nothing about the boundary itself is wrong.
    [Fact]
    public void AdmissionOnTheDateOfBirth_IsAcceptable()
    {
        Assert.Empty(EvaluateOrdering(
            AddJourneyDateRules.AdmissionDetailsPageId,
            AddJourneyDateRules.AdmissionDate,
            dateOfBirth: new DateOnly(2010, 9, 1),
            admissionDate: new DateOnly(2010, 9, 1)));
    }

    [Fact]
    public void OrderingRule_IsSkipped_WhenTheOtherPagesDateIsMissing()
    {
        Assert.Empty(EvaluateOrdering(
            AddJourneyDateRules.AdmissionDetailsPageId,
            AddJourneyDateRules.AdmissionDate,
            dateOfBirth: null,
            admissionDate: new DateOnly(2015, 9, 1)));
    }

    [Fact]
    public void OrderingRule_IsSkipped_WhenTheOtherPagesDateIsUnparseable()
    {
        var page = PageWith(AddJourneyDateRules.AdmissionDetailsPageId, AddJourneyDateRules.AdmissionDate);
        var answers = new Dictionary<string, QuestionAnswer>
        {
            [AddJourneyDateRules.AdmissionDate] = Answer(new DateOnly(2015, 9, 1)),
            // 31 February: the format rule owns this failure, and comparing against it would
            // replace that message with a comparison the user cannot make sense of.
            [AddJourneyDateRules.DateOfBirth] = new() { DateValue = new DateAnswer { Day = 31, Month = 2, Year = 2026 } }
        };

        Assert.Empty(AddJourneyDateRules.Evaluate(page, answers, Today, Pupil));
    }

    // A date that is independently wrong makes any comparison against it misleading, so the
    // future-date message is the one the user sees.
    [Fact]
    public void FutureDate_TakesPrecedenceOverTheOrderingMessage()
    {
        var violation = Assert.Single(EvaluateOrdering(
            AddJourneyDateRules.AdmissionDetailsPageId,
            AddJourneyDateRules.AdmissionDate,
            dateOfBirth: Today.AddDays(2),
            admissionDate: Today.AddDays(1)));

        Assert.Equal(AddJourneyDateRules.AdmissionDate, violation.QuestionId);
        Assert.Equal("Date Alice Smith was admitted to your school must be in the past", violation.Message);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static IReadOnlyList<DateFieldViolation> EvaluateOrdering(
        string pageId, string pageQuestionId, DateOnly? dateOfBirth, DateOnly? admissionDate)
    {
        var answers = new Dictionary<string, QuestionAnswer>();
        if (dateOfBirth is { } dob) answers[AddJourneyDateRules.DateOfBirth] = Answer(dob);
        if (admissionDate is { } admitted) answers[AddJourneyDateRules.AdmissionDate] = Answer(admitted);

        return AddJourneyDateRules.Evaluate(PageWith(pageId, pageQuestionId), answers, Today, Pupil);
    }

    private static JourneyPage PageWith(string pageId, string questionId) => new()
    {
        Id = pageId,
        Questions = [new Question { Id = questionId, Type = QuestionType.Date, Title = "Date" }]
    };

    private static QuestionAnswer Answer(DateOnly date) => new()
    {
        DateValue = new DateAnswer { Day = date.Day, Month = date.Month, Year = date.Year }
    };


    private static IReadOnlyList<DateFieldViolation> Evaluate(
        string pageId, string questionId, DateOnly? date)
    {
        var page = new JourneyPage
        {
            Id = pageId,
            Questions = [new Question { Id = questionId, Type = QuestionType.Date, Title = "Date" }]
        };

        var answers = date is null
            ? new Dictionary<string, QuestionAnswer>()
            : new Dictionary<string, QuestionAnswer>
            {
                [questionId] = new()
                {
                    DateValue = new DateAnswer
                    {
                        Day = date.Value.Day,
                        Month = date.Value.Month,
                        Year = date.Value.Year
                    }
                }
            };

        return AddJourneyDateRules.Evaluate(page, answers, Today, Pupil);
    }

    private static IReadOnlyList<DateFieldViolation> EvaluateAnswer(
        string pageId, string questionId, DateAnswer date)
    {
        var page = new JourneyPage
        {
            Id = pageId,
            Questions = [new Question { Id = questionId, Type = QuestionType.Date, Title = "Date" }]
        };

        var answers = new Dictionary<string, QuestionAnswer>
        {
            [questionId] = new() { DateValue = date }
        };

        return AddJourneyDateRules.Evaluate(page, answers, Today, Pupil);
    }
}
