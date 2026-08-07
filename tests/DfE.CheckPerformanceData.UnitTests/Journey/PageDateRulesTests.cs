using DfE.CheckPerformanceData.Application.Journey.DateRules;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

/// <summary>
/// The seven scenarios from AB#295246, against the invariant
/// <c>arrivedInEngland &lt;= firstEnglishSchool &lt;= startedAtSchool</c>, none in the future,
/// and the school start date on or after the cohort start.
/// </summary>
public sealed class PageDateRulesTests
{
    // A date inside a KS4 June checking window, so CohortStart is 1 September 2022 — the same
    // boundary the rules engine seeds for schoolAdmissionDate.
    private static readonly DateOnly Today = new(2026, 6, 15);

    private const string Pupil = "Alice Smith";

    // ── 001: started at this school before starting any English school ──────

    [Fact]
    public void Scenario001_StartedBeforeFirstEnglishSchool_FlagsBothFields()
    {
        var violations = Evaluate(a: new DateOnly(2023, 9, 4), b: new DateOnly(2024, 1, 8));

        Assert.Equal(2, violations.Count);
        Assert.Equal(
            "The date Alice Smith started at your school must be the same as or after " +
            "8 January 2024 when they first started at a school in England",
            MessageFor(violations, PageDateRules.StartedAtSchool));
        Assert.Equal(
            "The date Alice Smith first started at a school in England must be the same as or " +
            "before 4 September 2023 when they started at your school",
            MessageFor(violations, PageDateRules.FirstEnglishSchool));
    }

    // ── 002: started at this school before arriving in England ──────────────

    [Fact]
    public void Scenario002_StartedBeforeArrival_FlagsBothFields()
    {
        var violations = Evaluate(
            a: new DateOnly(2023, 9, 4), b: new DateOnly(2023, 9, 4), c: new DateOnly(2024, 1, 8));

        Assert.Equal(
            "The date Alice Smith started at your school must be the same as or after " +
            "8 January 2024 when they arrived in England",
            MessageFor(violations, PageDateRules.StartedAtSchool));
        Assert.Equal(
            "The date Alice Smith arrived in England must be the same as or before " +
            "4 September 2023 when they started at your school",
            MessageFor(violations, PageDateRules.ArrivedInEngland));
    }

    // ── 003: started at an English school before arriving in England ────────

    [Fact]
    public void Scenario003_FirstEnglishSchoolBeforeArrival_FlagsBothFields()
    {
        var violations = Evaluate(
            a: new DateOnly(2025, 1, 1), b: new DateOnly(2023, 9, 4), c: new DateOnly(2024, 1, 8));

        Assert.Equal(
            "The date Alice Smith first started at a school in England must be the same as or " +
            "after 8 January 2024 when they arrived in England",
            MessageFor(violations, PageDateRules.FirstEnglishSchool));
        Assert.Equal(
            "The date Alice Smith arrived in England must be the same as or before " +
            "4 September 2023 when they first started at a school in England",
            MessageFor(violations, PageDateRules.ArrivedInEngland));
    }

    // ── 004-006: no date may be in the future ───────────────────────────────

    [Fact]
    public void Scenario004_StartedInFuture_FlagsStartedAtSchool()
    {
        var violations = Evaluate(a: Today.AddDays(1), b: new DateOnly(2023, 9, 4));

        Assert.Equal(
            "The date Alice Smith started at your school must be in the past",
            MessageFor(violations, PageDateRules.StartedAtSchool));
    }

    [Fact]
    public void Scenario005_FirstEnglishSchoolInFuture_FlagsFirstEnglishSchool()
    {
        var violations = Evaluate(a: Today.AddDays(2), b: Today.AddDays(1));

        Assert.Equal(
            "The date Alice Smith first started at a school in England must be in the past",
            MessageFor(violations, PageDateRules.FirstEnglishSchool));
    }

    [Fact]
    public void Scenario006_ArrivalInFuture_FlagsArrivedInEngland()
    {
        var violations = Evaluate(a: Today, b: Today, c: Today.AddDays(1));

        Assert.Equal(
            "The date Alice Smith arrived in England must be in the past",
            MessageFor(violations, PageDateRules.ArrivedInEngland));
    }

    [Fact]
    public void Today_IsAcceptable_ForEveryField()
    {
        // The rule is "later than today", not "before today" — a pupil admitted this morning
        // is a legitimate answer.
        Assert.Empty(Evaluate(a: Today, b: Today, c: Today));
    }

    // ── 007: the school start date may not predate the cohort ───────────────

    [Fact]
    public void Scenario007_StartedBeforeCohort_FlagsStartedAtSchool()
    {
        var violations = Evaluate(a: new DateOnly(2022, 8, 31), b: new DateOnly(2022, 8, 31));

        Assert.Equal(
            "The date Alice Smith started at your school cannot be more than 4 years ago " +
            "when the cohort began",
            MessageFor(violations, PageDateRules.StartedAtSchool));
    }

    [Fact]
    public void Scenario007_StartedOnCohortStart_IsAcceptable()
    {
        Assert.Empty(Evaluate(a: new DateOnly(2022, 9, 1), b: new DateOnly(2022, 9, 1)));
    }

    [Fact]
    public void Scenario007_AppliesToStartedAtSchoolOnly()
    {
        // b. and c. may legitimately predate the cohort — this pupil was in England, at school,
        // before the cohort began; only the date they joined *this* school is bounded.
        Assert.Empty(Evaluate(
            a: new DateOnly(2023, 1, 9), b: new DateOnly(2019, 4, 1), c: new DateOnly(2018, 7, 20)));
    }

    /// <summary>
    /// Pins <see cref="PageDateRules.CohortStart"/> to the boundary the rules engine already
    /// seeds for the same field (<c>schoolAdmissionDate gte 2022-09-01</c>). If the engine's
    /// boundary moves and this does not, journey validation and auto-triage disagree: a request
    /// the form accepted would be auto-rejected downstream.
    /// </summary>
    [Theory]
    [InlineData(2026, 6, 15, 2022)]
    [InlineData(2026, 1, 5, 2022)]
    [InlineData(2027, 6, 15, 2023)]
    public void CohortStart_IsFirstOfSeptemberFourYearsBack(int year, int month, int day, int expectedYear)
    {
        Assert.Equal(new DateOnly(expectedYear, 9, 1), PageDateRules.CohortStart(new DateOnly(year, month, day)));
    }

    // ── Valid combinations ──────────────────────────────────────────────────

    [Fact]
    public void ConsistentDates_ProduceNoViolations()
    {
        Assert.Empty(Evaluate(
            a: new DateOnly(2024, 9, 2), b: new DateOnly(2023, 9, 4), c: new DateOnly(2023, 8, 20)));
    }

    [Fact]
    public void EqualDates_ProduceNoViolations()
    {
        // Arriving, starting school and joining this school on the same day is unusual but valid;
        // every message says "the same as or".
        var sameDay = new DateOnly(2023, 9, 4);

        Assert.Empty(Evaluate(a: sameDay, b: sameDay, c: sameDay));
    }

    // ── Missing and unusable dates ──────────────────────────────────────────

    [Fact]
    public void ArrivalOmitted_SkipsTheRulesThatNeedIt()
    {
        // c. is optional. 002, 003 and 006 cannot be evaluated without it; 001 still can.
        var violations = Evaluate(a: new DateOnly(2023, 9, 4), b: new DateOnly(2024, 1, 8), c: null);

        Assert.Equal(2, violations.Count);
        Assert.DoesNotContain(violations, v => v.QuestionId == PageDateRules.ArrivedInEngland);
    }

    [Fact]
    public void StartedAtSchoolOmitted_SkipsEveryComparisonInvolvingIt()
    {
        // a. failed its own format check, so it is unusable here. 003 does not involve it and
        // must still fire.
        var violations = Evaluate(a: null, b: new DateOnly(2023, 9, 4), c: new DateOnly(2024, 1, 8));

        Assert.Equal(2, violations.Count);
        Assert.DoesNotContain(violations, v => v.QuestionId == PageDateRules.StartedAtSchool);
    }

    [Fact]
    public void AllDatesOmitted_ProducesNoViolations()
    {
        Assert.Empty(Evaluate(a: null, b: null, c: null));
    }

    // ── Precedence ──────────────────────────────────────────────────────────

    [Fact]
    public void FutureDate_TakesPrecedenceOverSequenceRule()
    {
        // a. is both in the future and before b. Reporting "must be the same as or after
        // {b}" would send the user to fix the ordering when the real problem is the year.
        var violations = Evaluate(a: Today.AddYears(1), b: Today.AddYears(2));

        Assert.Equal(
            "The date Alice Smith started at your school must be in the past",
            MessageFor(violations, PageDateRules.StartedAtSchool));
    }

    [Fact]
    public void EachFieldGetsAtMostOneMessage()
    {
        // a. violates 001 and 002; b. violates 001 and 003; c. violates 002 and 003. Only the
        // first error per question is ever rendered, so only one may be returned.
        var violations = Evaluate(
            a: new DateOnly(2023, 1, 1), b: new DateOnly(2023, 6, 1), c: new DateOnly(2024, 1, 1));

        Assert.Equal(3, violations.Count);
        Assert.Equal(violations.Select(v => v.QuestionId).Distinct().Count(), violations.Count);
    }

    [Fact]
    public void Violations_AreOrderedAsTheQuestionsAppearOnThePage()
    {
        var violations = Evaluate(
            a: new DateOnly(2023, 1, 1), b: new DateOnly(2023, 6, 1), c: new DateOnly(2024, 1, 1));

        Assert.Equal(
            [PageDateRules.StartedAtSchool, PageDateRules.FirstEnglishSchool, PageDateRules.ArrivedInEngland],
            violations.Select(v => v.QuestionId));
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static IReadOnlyList<DateFieldViolation> Evaluate(
        DateOnly? a = null, DateOnly? b = null, DateOnly? c = null) =>
        PageDateRules.Evaluate(a, b, c, Today, Pupil);

    private static string? MessageFor(IReadOnlyList<DateFieldViolation> violations, string questionId) =>
        violations.SingleOrDefault(v => v.QuestionId == questionId)?.Message;
}
