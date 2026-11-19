using DfE.CheckPerformanceData.Application.RulesEngine;
using RulesEngineImpl = DfE.CheckPerformanceData.Application.RulesEngine.RulesEngine;

namespace DfE.CheckPerformanceData.Application.UnitTests.RulesEngine;

/// <summary>
/// #800: engine evaluation of window-relative date literals. Each leaf resolves
/// the literal from the context's window start date, never the clock. A
/// literal that cannot resolve renders a trace line and returns Unknown, so
/// the branch does not match and the request falls to otherwise (Scrutiny).
/// Fixed date literals are unchanged.
/// </summary>
public sealed class RulesEngineRelativeDateTests
{
    private readonly IRulesEngine _sut = new RulesEngineImpl();

    // --- resolution: the relative literal compares as its resolved date ---

    [Theory]
    [InlineData(2025, "2025-05-12", true)]  // second Monday of May 2025; equal is >=
    [InlineData(2025, "2025-05-11", false)] // day before
    [InlineData(2026, "2026-05-11", true)]  // second Monday of May 2026
    [InlineData(2027, "2027-05-10", true)]  // second Monday of May 2027
    [InlineData(2027, "2027-05-09", false)] // day before
    public void FieldCompare_Gte_SecondMondayOfMay_ResolvesPerWindowYear(int windowYear, string removalDate, bool expectReject)
    {
        var rules = OneOutcome("X",
            Branch("R1", DecisionStatus.AutoRejected,
                new Predicate.FieldCompare("dateOfRemoval", CompareOp.Gte, new FieldValue.RelativeDate(new RelativeDateSpec.SecondMondayOfMay()))),
            Branch("OW", DecisionStatus.Scrutiny, Predicate.Otherwise.Instance));

        var ctx = Ctx("X", "KS2", new DateOnly(windowYear, 9, 3),
            ("dateOfRemoval", new FieldValue.Date(DateOnly.Parse(removalDate))));

        var d = _sut.Evaluate(rules, ctx, Lookups.Empty);

        var expected = expectReject ? DecisionStatus.AutoRejected : DecisionStatus.Scrutiny;
        Assert.Equal(expected, d.Status);
    }

    [Fact]
    public void FieldEq_RelativeLiteral_ResolvesAndMatches()
    {
        // dayMonthOfWindowYear 1-16 with a 2025 window resolves to 2025-01-16.
        var rules = OneOutcome("X",
            Branch("R1", DecisionStatus.AutoApproved,
                new Predicate.FieldEq("dateOfRemoval", new FieldValue.RelativeDate(new RelativeDateSpec.DayMonthOfWindowYear(1, 16)))),
            Branch("OW", DecisionStatus.Scrutiny, Predicate.Otherwise.Instance));

        var ctx = Ctx("X", "KS4June", new DateOnly(2025, 9, 2),
            ("dateOfRemoval", new FieldValue.Date(new DateOnly(2025, 1, 16))));

        var d = _sut.Evaluate(rules, ctx, Lookups.Empty);

        Assert.Equal(DecisionStatus.AutoApproved, d.Status);
        Assert.Equal("R1", d.MatchedRuleId);
    }

    [Fact]
    public void FieldNeq_RelativeLiteral_ResolvesAndMatches()
    {
        // secondMondayOfMay with a 2027 window resolves to 2027-05-10; any other date is !=.
        var rules = OneOutcome("X",
            Branch("R1", DecisionStatus.AutoApproved,
                new Predicate.FieldNeq("dateOfRemoval", new FieldValue.RelativeDate(new RelativeDateSpec.SecondMondayOfMay()))),
            Branch("OW", DecisionStatus.Scrutiny, Predicate.Otherwise.Instance));

        var ctx = Ctx("X", "KS2", new DateOnly(2027, 9, 3),
            ("dateOfRemoval", new FieldValue.Date(new DateOnly(2027, 5, 11))));

        var d = _sut.Evaluate(rules, ctx, Lookups.Empty);

        Assert.Equal(DecisionStatus.AutoApproved, d.Status);
        Assert.Equal("R1", d.MatchedRuleId);
    }

    [Fact]
    public void FieldIn_RelativeLiteral_ResolvesAndMatches()
    {
        // in [secondMondayOfMay(2027) => 2027-05-10, fixed 2027-05-09]
        var rules = OneOutcome("X",
            Branch("R1", DecisionStatus.AutoApproved,
                new Predicate.FieldIn("dateOfRemoval", new FieldValue[]
                {
                    new FieldValue.RelativeDate(new RelativeDateSpec.SecondMondayOfMay()),
                    new FieldValue.Date(new DateOnly(2027, 5, 9)),
                })),
            Branch("OW", DecisionStatus.Scrutiny, Predicate.Otherwise.Instance));

        var ctx = Ctx("X", "KS2", new DateOnly(2027, 9, 3),
            ("dateOfRemoval", new FieldValue.Date(new DateOnly(2027, 5, 10))));

        var d = _sut.Evaluate(rules, ctx, Lookups.Empty);

        Assert.Equal(DecisionStatus.AutoApproved, d.Status);
        Assert.Equal("R1", d.MatchedRuleId);
    }

    [Fact]
    public void FieldCompare_RelativeLiteral_Feb29ResolvesInALeapWindowYear()
    {
        var rules = OneOutcome("X",
            Branch("R1", DecisionStatus.AutoApproved,
                new Predicate.FieldEq("dateOfRemoval", new FieldValue.RelativeDate(new RelativeDateSpec.DayMonthOfWindowYear(2, 29)))),
            Branch("OW", DecisionStatus.Scrutiny, Predicate.Otherwise.Instance));

        var ctx = Ctx("X", "KS2", new DateOnly(2028, 9, 4),
            ("dateOfRemoval", new FieldValue.Date(new DateOnly(2028, 2, 29))));

        var d = _sut.Evaluate(rules, ctx, Lookups.Empty);

        Assert.Equal(DecisionStatus.AutoApproved, d.Status);
        Assert.Equal("R1", d.MatchedRuleId);
    }

    // --- fail closed: unresolvable -> Unknown -> otherwise (Scrutiny) ---

    [Fact]
    public void FieldCompare_RelativeLiteral_NoWindowStartDate_FallsToOtherwise()
    {
        var rules = OneOutcome("X",
            Branch("R1", DecisionStatus.AutoRejected,
                new Predicate.FieldCompare("dateOfRemoval", CompareOp.Gte, new FieldValue.RelativeDate(new RelativeDateSpec.SecondMondayOfMay()))),
            Branch("OW", DecisionStatus.Scrutiny, Predicate.Otherwise.Instance));

        var ctx = Ctx("X", "KS2", null,
            ("dateOfRemoval", new FieldValue.Date(new DateOnly(2027, 5, 10))));

        var d = _sut.Evaluate(rules, ctx, Lookups.Empty);

        Assert.Equal(DecisionStatus.Scrutiny, d.Status);
        Assert.Equal("OW", d.MatchedRuleId);
    }

    [Fact]
    public void FieldCompare_RelativeLiteral_Feb29InNonLeapWindowYear_FallsToOtherwise()
    {
        var rules = OneOutcome("X",
            Branch("R1", DecisionStatus.AutoRejected,
                new Predicate.FieldEq("dateOfRemoval", new FieldValue.RelativeDate(new RelativeDateSpec.DayMonthOfWindowYear(2, 29)))),
            Branch("OW", DecisionStatus.Scrutiny, Predicate.Otherwise.Instance));

        var ctx = Ctx("X", "KS2", new DateOnly(2027, 9, 3),
            ("dateOfRemoval", new FieldValue.Date(new DateOnly(2027, 2, 28))));

        var d = _sut.Evaluate(rules, ctx, Lookups.Empty);

        Assert.Equal(DecisionStatus.Scrutiny, d.Status);
        Assert.Equal("OW", d.MatchedRuleId);
    }

    [Fact]
    public void FieldNeq_RelativeLiteral_Unresolvable_DoesNotAutoDecide()
    {
        // neq on an unresolvable literal must stay Unknown, never true.
        var rules = OneOutcome("X",
            Branch("R1", DecisionStatus.AutoApproved,
                new Predicate.FieldNeq("dateOfRemoval", new FieldValue.RelativeDate(new RelativeDateSpec.SecondMondayOfMay()))),
            Branch("OW", DecisionStatus.Scrutiny, Predicate.Otherwise.Instance));

        var ctx = Ctx("X", "KS2", null,
            ("dateOfRemoval", new FieldValue.Date(new DateOnly(2027, 5, 11))));

        var d = _sut.Evaluate(rules, ctx, Lookups.Empty);

        Assert.Equal(DecisionStatus.Scrutiny, d.Status);
        Assert.Equal("OW", d.MatchedRuleId);
    }

    [Fact]
    public void Unresolvable_RelativeLiteral_RendersATraceLineNamingTheForm()
    {
        // The unresolvable leaf returns Unknown; the second leaf matches, so the
        // branch matches and the matched-branch trace carries the trace line.
        var rules = OneOutcome("X",
            Branch("R1", DecisionStatus.AutoApproved,
                new Predicate.AnyOf(new Predicate[]
                {
                    new Predicate.FieldCompare("dateOfRemoval", CompareOp.Gte, new FieldValue.RelativeDate(new RelativeDateSpec.SecondMondayOfMay())),
                    new Predicate.FieldEq("checkingWindowType", new FieldValue.Str("KS2")),
                })),
            Branch("OW", DecisionStatus.Scrutiny, Predicate.Otherwise.Instance));

        var ctx = Ctx("X", "KS2", null,
            ("dateOfRemoval", new FieldValue.Date(new DateOnly(2027, 5, 10))));

        var d = _sut.Evaluate(rules, ctx, Lookups.Empty);

        Assert.Equal(DecisionStatus.AutoApproved, d.Status);
        Assert.Equal("R1", d.MatchedRuleId);
        Assert.Contains(d.Trace, line => line.Contains("window-relative: secondMondayOfMay", StringComparison.Ordinal));
    }

    // A resolved relative literal keeps its note in the matched-branch trace, so an
    // auditor can tell the cutoff came from the window, not a fixed date (#800).
    [Fact]
    public void Resolved_RelativeLiteral_RendersTheResolvedDateAndTheFormNameInTheTrace()
    {
        var rules = OneOutcome("X",
            Branch("R1", DecisionStatus.AutoApproved,
                new Predicate.FieldCompare("dateOfRemoval", CompareOp.Gte, new FieldValue.RelativeDate(new RelativeDateSpec.SecondMondayOfMay()))),
            Branch("OW", DecisionStatus.Scrutiny, Predicate.Otherwise.Instance));

        // secondMondayOfMay with a 2027 window resolves to 2027-05-10.
        var ctx = Ctx("X", "KS2", new DateOnly(2027, 9, 3),
            ("dateOfRemoval", new FieldValue.Date(new DateOnly(2027, 5, 10))));

        var d = _sut.Evaluate(rules, ctx, Lookups.Empty);

        Assert.Equal(DecisionStatus.AutoApproved, d.Status);
        Assert.Equal("R1", d.MatchedRuleId);
        Assert.Contains(d.Trace, line =>
            line.Contains("2027-05-10", StringComparison.Ordinal)
            && line.Contains("window-relative: secondMondayOfMay", StringComparison.Ordinal));
    }

    // A window start date whose year is not far enough ahead of the offset has no date.
    // Year 1 with a 3-year offset lands in year -2, which DateOnly cannot hold, so the
    // resolver returns false instead of throwing (the worker must not crash on it).
    [Fact]
    public void FirstSeptemberMinusYears_OffsetBeyondYearOne_DoesNotResolve()
    {
        Assert.False(RelativeDateResolver.TryResolve(
            new RelativeDateSpec.FirstSeptemberMinusYears(3), new DateOnly(1, 9, 1), out _));
    }

    [Fact]
    public void FirstSeptemberMinusYears_JustBeyondYearOne_DoesResolve()
    {
        // Year 3 minus 3 is year 0... which is invalid; year 4 minus 3 is year 1, valid.
        Assert.True(RelativeDateResolver.TryResolve(
            new RelativeDateSpec.FirstSeptemberMinusYears(3), new DateOnly(4, 9, 1), out var date));
        Assert.Equal(new DateOnly(1, 9, 1), date);
    }

    // --- fixed date literals are unchanged ---

    [Fact]
    public void FieldCompare_FixedDateLiteral_StillWorks_WithoutAWindowStartDate()
    {
        var rules = OneOutcome("X",
            Branch("R1", DecisionStatus.AutoRejected,
                new Predicate.FieldCompare("dateOfRemoval", CompareOp.Gt, new FieldValue.Date(new DateOnly(2025, 1, 16)))),
            Branch("OW", DecisionStatus.Scrutiny, Predicate.Otherwise.Instance));

        var ctx = Ctx("X", "KS2", null,
            ("dateOfRemoval", new FieldValue.Date(new DateOnly(2025, 1, 17))));

        var d = _sut.Evaluate(rules, ctx, Lookups.Empty);

        Assert.Equal(DecisionStatus.AutoRejected, d.Status);
        Assert.Equal("R1", d.MatchedRuleId);
    }

    // =====================================================================
    // builders
    // =====================================================================

    private static RuleSet OneOutcome(string key, params RuleBranch[] branches) =>
        new("test", DateTimeOffset.UtcNow, new[] { new OutcomeRules(key, key, branches) });

    private static RuleBranch Branch(string id, DecisionStatus status, Predicate when) =>
        new(id, status, when);

    private static RuleContext Ctx(string outcomeKey, string checkingWindowType, DateOnly? windowStartDate, params (string, FieldValue)[] extraFields)
    {
        var fields = new Dictionary<string, FieldValue>
        {
            ["checkingWindowType"] = new FieldValue.Str(checkingWindowType),
            ["requestType"]        = new FieldValue.Str(outcomeKey),
        };
        foreach (var (k, v) in extraFields) fields[k] = v;
        return new RuleContext(outcomeKey, checkingWindowType, fields, windowStartDate);
    }
}
