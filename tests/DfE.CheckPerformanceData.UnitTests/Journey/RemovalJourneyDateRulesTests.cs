using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.Journey.DateRules;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

/// <summary>
/// The six KS4 removal journeys (scenarios 001-006 in the "Remove" flow). Each page carries a
/// single removal/exclusion date which must not be later than today — a future date is rejected
/// with the journey-specific "Date … must be in the past" message, while today and any past date
/// are acceptable. A blank, part-filled or impossible date is not this rule's to report: the
/// per-question format rules own that failure.
/// </summary>
public sealed class RemovalJourneyDateRulesTests
{
    // A date inside a KS4 June checking window, so the "future" side of the boundary is clear.
    private static readonly DateOnly Today = new(2026, 6, 15);

    private const string Pupil = "Alice Smith";

    private const string RollWording = "Date Alice Smith was removed from your school roll must be in the past";

    // ── Each journey flags its own future date with its exact spec message ──

    [Fact]
    public void Scenario001_PermanentExclusion_FutureDate_FlagsDatePupilExcluded()
    {
        var violation = Assert.Single(Evaluate(
            RemovalJourneyDateRules.PermanentExclusionPageId,
            RemovalJourneyDateRules.DatePupilExcluded,
            Today.AddDays(1)));

        Assert.Equal(RemovalJourneyDateRules.DatePupilExcluded, violation.QuestionId);
        Assert.Equal("Date Alice Smith was excluded must be in the past", violation.Message);
    }

    [Fact]
    public void Scenario002_ChildMissingEducation_FutureDate_FlagsDateRemovedFromRoll()
    {
        var violation = Assert.Single(Evaluate(
            RemovalJourneyDateRules.ChildMissingEducationPageId,
            RemovalJourneyDateRules.DateRemovedFromRoll,
            Today.AddDays(1)));

        Assert.Equal(RemovalJourneyDateRules.DateRemovedFromRoll, violation.QuestionId);
        Assert.Equal(RollWording, violation.Message);
    }

    [Fact]
    public void Scenario003_PupilDied_FutureDate_FlagsDateRemovedFromRoll()
    {
        var violation = Assert.Single(Evaluate(
            RemovalJourneyDateRules.PupilDiedPageId,
            RemovalJourneyDateRules.DateRemovedFromRoll,
            Today.AddDays(1)));

        Assert.Equal(RemovalJourneyDateRules.DateRemovedFromRoll, violation.QuestionId);
        Assert.Equal(RollWording, violation.Message);
    }

    [Fact]
    public void Scenario004_ElectiveHomeEducation_FutureDate_FlagsDateRemovedFromRoll()
    {
        var violation = Assert.Single(Evaluate(
            RemovalJourneyDateRules.ElectiveHomeEducationPageId,
            RemovalJourneyDateRules.DateRemovedFromRoll,
            Today.AddDays(1)));

        Assert.Equal(RemovalJourneyDateRules.DateRemovedFromRoll, violation.QuestionId);
        Assert.Equal(RollWording, violation.Message);
    }

    [Fact]
    public void Scenario005_PermanentlyExcluded_FutureDate_FlagsDatePermanentlyExcluded()
    {
        var violation = Assert.Single(Evaluate(
            RemovalJourneyDateRules.PermanentlyExcludedPageId,
            RemovalJourneyDateRules.DatePermanentlyExcluded,
            Today.AddDays(1)));

        Assert.Equal(RemovalJourneyDateRules.DatePermanentlyExcluded, violation.QuestionId);
        Assert.Equal(
            "Date Alice Smith was permanently excluded from your school must be in the past",
            violation.Message);
    }

    [Fact]
    public void Scenario006_PermanentlyLeftEngland_FutureDate_FlagsDateRemovedFromRoll()
    {
        var violation = Assert.Single(Evaluate(
            RemovalJourneyDateRules.PermanentlyLeftEnglandPageId,
            RemovalJourneyDateRules.DateRemovedFromRoll,
            Today.AddDays(1)));

        Assert.Equal(RemovalJourneyDateRules.DateRemovedFromRoll, violation.QuestionId);
        Assert.Equal(RollWording, violation.Message);
    }

    // ── Today and the past are acceptable on every journey ─────────────────

    [Theory]
    [InlineData(nameof(RemovalJourneyDateRules.PermanentExclusionPageId), nameof(RemovalJourneyDateRules.DatePupilExcluded))]
    [InlineData(nameof(RemovalJourneyDateRules.ChildMissingEducationPageId), nameof(RemovalJourneyDateRules.DateRemovedFromRoll))]
    [InlineData(nameof(RemovalJourneyDateRules.PupilDiedPageId), nameof(RemovalJourneyDateRules.DateRemovedFromRoll))]
    [InlineData(nameof(RemovalJourneyDateRules.ElectiveHomeEducationPageId), nameof(RemovalJourneyDateRules.DateRemovedFromRoll))]
    [InlineData(nameof(RemovalJourneyDateRules.PermanentlyExcludedPageId), nameof(RemovalJourneyDateRules.DatePermanentlyExcluded))]
    [InlineData(nameof(RemovalJourneyDateRules.PermanentlyLeftEnglandPageId), nameof(RemovalJourneyDateRules.DateRemovedFromRoll))]
    public void Today_IsAcceptable_OnEveryRemovalPage(string pageIdField, string questionIdField)
    {
        var pageId = (string)typeof(RemovalJourneyDateRules).GetField(pageIdField)!.GetValue(null)!;
        var questionId = (string)typeof(RemovalJourneyDateRules).GetField(questionIdField)!.GetValue(null)!;

        // The rule is "later than today", not "before today" — a pupil removed from the roll
        // today is a legitimate answer.
        Assert.Empty(Evaluate(pageId, questionId, Today));
    }

    [Fact]
    public void PastDate_IsAcceptable()
    {
        Assert.Empty(Evaluate(
            RemovalJourneyDateRules.PupilDiedPageId,
            RemovalJourneyDateRules.DateRemovedFromRoll,
            Today.AddDays(-365)));
    }

    [Theory]
    [InlineData(nameof(RemovalJourneyDateRules.PermanentExclusionPageId), nameof(RemovalJourneyDateRules.DatePupilExcluded))]
    [InlineData(nameof(RemovalJourneyDateRules.ChildMissingEducationPageId), nameof(RemovalJourneyDateRules.DateRemovedFromRoll))]
    [InlineData(nameof(RemovalJourneyDateRules.PupilDiedPageId), nameof(RemovalJourneyDateRules.DateRemovedFromRoll))]
    [InlineData(nameof(RemovalJourneyDateRules.ElectiveHomeEducationPageId), nameof(RemovalJourneyDateRules.DateRemovedFromRoll))]
    [InlineData(nameof(RemovalJourneyDateRules.PermanentlyExcludedPageId), nameof(RemovalJourneyDateRules.DatePermanentlyExcluded))]
    [InlineData(nameof(RemovalJourneyDateRules.PermanentlyLeftEnglandPageId), nameof(RemovalJourneyDateRules.DateRemovedFromRoll))]
    public void HistoricallyDistantPastDate_IsAcceptable_OnEveryRemovalPage(string pageIdField, string questionIdField)
    {
        // US3: no lower limit — a removal a decade and a half ago is a legitimate answer.
        var pageId = (string)typeof(RemovalJourneyDateRules).GetField(pageIdField)!.GetValue(null)!;
        var questionId = (string)typeof(RemovalJourneyDateRules).GetField(questionIdField)!.GetValue(null)!;

        Assert.Empty(Evaluate(pageId, questionId, new DateOnly(2010, 9, 1)));
    }

    [Fact]
    public void LeapDayOnNonLeapYear_ProducesNoViolation()
    {
        // 29 February 2025 does not exist. It is an unparsable date, so the future-date rule must
        // not flag it (and must not throw) — the per-question invalid-date rule owns that case.
        var violations = EvaluateAnswer(
            RemovalJourneyDateRules.PupilDiedPageId,
            RemovalJourneyDateRules.DateRemovedFromRoll,
            new DateAnswer { Day = 29, Month = 2, Year = 2025 });

        Assert.Empty(violations);
    }

    [Fact]
    public void LeapDayOnLeapYear_IsARealDate()
    {
        // 29 February 2024 exists and is in the past — accepted, proving the rule has no
        // lower-limit or leap-day special case beyond "is it a real date".
        Assert.Empty(Evaluate(
            RemovalJourneyDateRules.PupilDiedPageId,
            RemovalJourneyDateRules.DateRemovedFromRoll,
            new DateOnly(2024, 2, 29)));
    }

    // ── Missing and unusable dates are not this rule's to report ────────────

    [Fact]
    public void BlankDate_ProducesNoViolation()
    {
        Assert.Empty(Evaluate(
            RemovalJourneyDateRules.PupilDiedPageId,
            RemovalJourneyDateRules.DateRemovedFromRoll,
            date: null));
    }

    [Fact]
    public void IncompleteDate_ProducesNoViolation()
    {
        // The per-question format rules own the "part-filled" failure.
        var violations = EvaluateAnswer(
            RemovalJourneyDateRules.PupilDiedPageId,
            RemovalJourneyDateRules.DateRemovedFromRoll,
            new DateAnswer { Day = 0, Month = 0, Year = 0 });

        Assert.Empty(violations);
    }

    [Fact]
    public void ImpossibleCalendarDate_ProducesNoViolation()
    {
        // 31 February does not exist — the format rules report that, not this rule.
        var violations = EvaluateAnswer(
            RemovalJourneyDateRules.PupilDiedPageId,
            RemovalJourneyDateRules.DateRemovedFromRoll,
            new DateAnswer { Day = 31, Month = 2, Year = 2026 });

        Assert.Empty(violations);
    }

    // ── Page structure ──────────────────────────────────────────────────────

    [Fact]
    public void NonDateQuestionsOnThePage_AreIgnored()
    {
        var page = new JourneyPage
        {
            Id = RemovalJourneyDateRules.PupilDiedPageId,
            Questions =
            [
                new Question { Id = "reason", Type = QuestionType.Radio, Title = "Reason" },
                new Question { Id = RemovalJourneyDateRules.DateRemovedFromRoll, Type = QuestionType.Date, Title = "Date" }
            ]
        };

        var answers = new Dictionary<string, QuestionAnswer>
        {
            ["reason"] = new() { TextValue = "pupil-died" },
            [RemovalJourneyDateRules.DateRemovedFromRoll] = new()
            {
                DateValue = new DateAnswer { Day = 16, Month = 6, Year = 2026 }
            }
        };

        var violations = RemovalJourneyDateRules.EvaluateFutureDates(page, answers, Today, Pupil);

        var violation = Assert.Single(violations);
        Assert.Equal(RemovalJourneyDateRules.DateRemovedFromRoll, violation.QuestionId);
    }

    [Fact]
    public void Violations_AreOrderedAsTheQuestionsAppearOnThePage()
    {
        // A page cannot carry two of these questions today, but the ordering contract still
        // holds if the config ever allows it: violations come back in page order.
        var page = new JourneyPage
        {
            Id = RemovalJourneyDateRules.PermanentExclusionPageId,
            Questions =
            [
                new Question { Id = RemovalJourneyDateRules.DatePupilExcluded, Type = QuestionType.Date, Title = "Exclusion date" },
                new Question { Id = RemovalJourneyDateRules.DateRemovedFromRoll, Type = QuestionType.Date, Title = "Roll date" }
            ]
        };

        var answers = new Dictionary<string, QuestionAnswer>
        {
            [RemovalJourneyDateRules.DatePupilExcluded] = new()
            {
                DateValue = new DateAnswer { Day = 16, Month = 6, Year = 2026 }
            },
            [RemovalJourneyDateRules.DateRemovedFromRoll] = new()
            {
                DateValue = new DateAnswer { Day = 17, Month = 6, Year = 2026 }
            }
        };

        var violations = RemovalJourneyDateRules.EvaluateFutureDates(page, answers, Today, Pupil);

        Assert.Equal(
            [RemovalJourneyDateRules.DatePupilExcluded, RemovalJourneyDateRules.DateRemovedFromRoll],
            violations.Select(v => v.QuestionId));
    }

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

        return RemovalJourneyDateRules.EvaluateFutureDates(page, answers, Today, Pupil);
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

        return RemovalJourneyDateRules.EvaluateFutureDates(page, answers, Today, Pupil);
    }
}
