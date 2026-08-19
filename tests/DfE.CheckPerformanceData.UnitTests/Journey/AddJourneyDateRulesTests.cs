using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.Journey.DateRules;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

/// <summary>
/// The Add-a-pupil journey (AB#297310). Two dates — date of birth (learner-details) and
/// admission date (admission-details) — must not be later than today. A future date is
/// rejected with a "must be in the past" message; today and any past date are acceptable.
/// A blank, part-filled or impossible date is not this rule's to report: the per-question
/// format rules own that failure.
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

    // ── Helpers ─────────────────────────────────────────────────────────────

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

        return AddJourneyDateRules.EvaluateFutureDates(page, answers, Today, Pupil);
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

        return AddJourneyDateRules.EvaluateFutureDates(page, answers, Today, Pupil);
    }
}
