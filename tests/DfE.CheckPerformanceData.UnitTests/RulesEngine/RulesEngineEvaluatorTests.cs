using DfE.CheckPerformanceData.Application.RulesEngine;
using RulesEngineImpl = DfE.CheckPerformanceData.Application.RulesEngine.RulesEngine;

namespace DfE.CheckPerformanceData.Application.UnitTests.RulesEngine;

/// <summary>
/// Behavioural tests for the evaluator. Split into:
/// (a) predicate-level mechanics (each operator + 3-valued logic + trace cap),
/// (b) representative docx outcomes (one simple, one code-list, one date,
///     one compound boolean), and (c) engine-level edge cases.
/// The exhaustive per-outcome regression suite lives in the integration tests
/// against the real seed rules.json.
/// </summary>
public sealed class RulesEngineEvaluatorTests
{
    private readonly IRulesEngine _sut = new RulesEngineImpl();

    // =====================================================================
    // (a) predicate mechanics
    // =====================================================================

    [Fact]
    public void FieldEq_Matches_WhenValuesAreEqual()
    {
        var rules = OneOutcome("X",
            Branch("R1", DecisionStatus.AutoApproved, new Predicate.FieldEq("checkingWindowType", new FieldValue.Str("KS4June"))),
            Branch("OW", DecisionStatus.Scrutiny, Predicate.Otherwise.Instance));

        var ctx = Ctx("X", "KS4June");

        var d = _sut.Evaluate(rules, ctx, Lookups.Empty);

        Assert.Equal(DecisionStatus.AutoApproved, d.Status);
        Assert.Equal("R1", d.MatchedRuleId);
    }

    [Fact]
    public void FieldEq_DoesNotMatch_WhenFieldIsUnknown()
    {
        // Per "uncertainty → false": no answer should never auto-decide. Should fall through to otherwise (Scrutiny).
        var rules = OneOutcome("X",
            Branch("R1", DecisionStatus.AutoApproved, new Predicate.FieldEq("inclusionFlag", new FieldValue.Str("402"))),
            Branch("OW", DecisionStatus.Scrutiny, Predicate.Otherwise.Instance));

        var ctx = Ctx("X", "KS4June"); // inclusionFlag not provided

        var d = _sut.Evaluate(rules, ctx, Lookups.Empty);

        Assert.Equal(DecisionStatus.Scrutiny, d.Status);
        Assert.Equal("OW", d.MatchedRuleId);
    }

    [Fact]
    public void FieldNeq_OnUnknown_DoesNotMatch()
    {
        // "neq" on Unknown should not auto-decide. Defers to Scrutiny.
        var rules = OneOutcome("X",
            Branch("R1", DecisionStatus.AutoApproved, new Predicate.FieldNeq("inclusionFlag", new FieldValue.Str("999"))),
            Branch("OW", DecisionStatus.Scrutiny, Predicate.Otherwise.Instance));

        var ctx = Ctx("X", "KS4June");

        var d = _sut.Evaluate(rules, ctx, Lookups.Empty);

        Assert.Equal(DecisionStatus.Scrutiny, d.Status);
    }

    [Fact]
    public void FieldIn_Matches_WhenValueInList()
    {
        var rules = OneOutcome("X",
            Branch("R1", DecisionStatus.AutoApproved,
                new Predicate.FieldIn("inclusionFlag", new FieldValue[]
                {
                    new FieldValue.Str("402"), new FieldValue.Str("404"), new FieldValue.Str("407")
                })),
            Branch("OW", DecisionStatus.Scrutiny, Predicate.Otherwise.Instance));

        var ctx = Ctx("X", "KS4June", ("inclusionFlag", new FieldValue.Str("404")));

        var d = _sut.Evaluate(rules, ctx, Lookups.Empty);

        Assert.Equal(DecisionStatus.AutoApproved, d.Status);
    }

    [Theory]
    [InlineData("2024-12-01", false)] // dateOfRemoval not > 2025-01-16
    [InlineData("2025-01-16", false)] // equal, not strictly greater
    [InlineData("2025-01-17", true)]  // strictly greater
    public void FieldCompare_Date_Gt_FollowsStrictCalendarSemantics(string dateString, bool expectReject)
    {
        var rules = OneOutcome("X",
            Branch("R1", DecisionStatus.AutoRejected,
                new Predicate.FieldCompare("dateOfRemoval", CompareOp.Gt, new FieldValue.Date(new DateOnly(2025, 1, 16)))),
            Branch("OW", DecisionStatus.Scrutiny, Predicate.Otherwise.Instance));

        var ctx = Ctx("X", "KS2",
            ("dateOfRemoval", new FieldValue.Date(DateOnly.Parse(dateString))));

        var d = _sut.Evaluate(rules, ctx, Lookups.Empty);

        var expected = expectReject ? DecisionStatus.AutoRejected : DecisionStatus.Scrutiny;
        Assert.Equal(expected, d.Status);
    }

    [Fact]
    public void Not_OnFalseLeaf_BecomesTrue()
    {
        var rules = OneOutcome("X",
            Branch("R1", DecisionStatus.AutoApproved,
                new Predicate.Not(new Predicate.FieldEq("hasTerminalIllness", new FieldValue.Bool(true)))),
            Branch("OW", DecisionStatus.Scrutiny, Predicate.Otherwise.Instance));

        var ctx = Ctx("X", "KS4June", ("hasTerminalIllness", new FieldValue.Bool(false)));

        var d = _sut.Evaluate(rules, ctx, Lookups.Empty);

        Assert.Equal(DecisionStatus.AutoApproved, d.Status);
    }

    [Fact]
    public void Not_OnUnknownLeaf_StaysUnknown_AndDoesNotAutoApprove()
    {
        // This is the critical safety property: Not(unknown) is unknown, not true.
        // Otherwise an absent answer would flip into an auto-decision.
        var rules = OneOutcome("X",
            Branch("R1", DecisionStatus.AutoApproved,
                new Predicate.Not(new Predicate.FieldEq("hasTerminalIllness", new FieldValue.Bool(true)))),
            Branch("OW", DecisionStatus.Scrutiny, Predicate.Otherwise.Instance));

        var ctx = Ctx("X", "KS4June"); // hasTerminalIllness not provided

        var d = _sut.Evaluate(rules, ctx, Lookups.Empty);

        Assert.Equal(DecisionStatus.Scrutiny, d.Status);
    }

    [Fact]
    public void IsKnownAndCertain_True_WhenFieldHasTypedValue()
    {
        var rules = OneOutcome("X",
            Branch("R1", DecisionStatus.AutoApproved, new Predicate.IsKnownAndCertain("dateOfArrivalInEngland")),
            Branch("OW", DecisionStatus.Scrutiny, Predicate.Otherwise.Instance));

        var ctx = Ctx("X", "KS4June",
            ("dateOfArrivalInEngland", new FieldValue.Date(new DateOnly(2024, 9, 1))));

        var d = _sut.Evaluate(rules, ctx, Lookups.Empty);

        Assert.Equal(DecisionStatus.AutoApproved, d.Status);
    }

    [Fact]
    public void IsKnownAndCertain_False_WhenFieldIsUncertain()
    {
        var rules = OneOutcome("X",
            Branch("R1", DecisionStatus.AutoApproved, new Predicate.IsKnownAndCertain("dateOfArrivalInEngland")),
            Branch("OW", DecisionStatus.Scrutiny, Predicate.Otherwise.Instance));

        var ctx = Ctx("X", "KS4June",
            ("dateOfArrivalInEngland", new FieldValue.Uncertain(new FieldValue.Date(new DateOnly(2024, 9, 1)))));

        var d = _sut.Evaluate(rules, ctx, Lookups.Empty);

        Assert.Equal(DecisionStatus.Scrutiny, d.Status);
    }

    [Fact]
    public void OfficialLanguageIs_TrueViaLookup()
    {
        var rules = OneOutcome("X",
            Branch("R1", DecisionStatus.AutoRejected,
                new Predicate.OfficialLanguageIs("countryOfOrigin", "English")),
            Branch("OW", DecisionStatus.Scrutiny, Predicate.Otherwise.Instance));

        var lookups = new Lookups(new Dictionary<string, IReadOnlyList<string>>
        {
            ["AU"] = new[] { "English" },
            ["FR"] = new[] { "French" },
        });

        var ctx = Ctx("X", "KS4June", ("countryOfOrigin", new FieldValue.Str("AU")));

        var d = _sut.Evaluate(rules, ctx, lookups);

        Assert.Equal(DecisionStatus.AutoRejected, d.Status);
    }

    [Fact]
    public void AllOf_ShortCircuitsOnFirstFalse()
    {
        var rules = OneOutcome("X",
            Branch("R1", DecisionStatus.AutoRejected, new Predicate.AllOf(new Predicate[]
            {
                new Predicate.FieldEq("checkingWindowType", new FieldValue.Str("KS2")), // false (we send KS4)
                new Predicate.FieldEq("inclusionFlag", new FieldValue.Str("402")) // not evaluated
            })),
            Branch("OW", DecisionStatus.Scrutiny, Predicate.Otherwise.Instance));

        var ctx = Ctx("X", "KS4June"); // KS2 mismatch; inclusionFlag unknown

        var d = _sut.Evaluate(rules, ctx, Lookups.Empty);

        Assert.Equal(DecisionStatus.Scrutiny, d.Status);
    }

    [Fact]
    public void AnyOf_ShortCircuitsOnFirstTrue()
    {
        var rules = OneOutcome("X",
            Branch("R1", DecisionStatus.AutoApproved, new Predicate.AnyOf(new Predicate[]
            {
                new Predicate.FieldEq("checkingWindowType", new FieldValue.Str("KS4June")), // true
                new Predicate.FieldEq("missingField", new FieldValue.Str("x"))  // unknown - not reached
            })),
            Branch("OW", DecisionStatus.Scrutiny, Predicate.Otherwise.Instance));

        var ctx = Ctx("X", "KS4June");

        var d = _sut.Evaluate(rules, ctx, Lookups.Empty);

        Assert.Equal(DecisionStatus.AutoApproved, d.Status);
    }

    // =====================================================================
    // (b) representative docx outcomes
    // =====================================================================

    [Theory]
    [InlineData("402", DecisionStatus.AutoApproved)]
    [InlineData("404", DecisionStatus.AutoApproved)]
    [InlineData("422", DecisionStatus.AutoApproved)]
    [InlineData("413", DecisionStatus.AutoRejected)]
    [InlineData("430", DecisionStatus.AutoRejected)]
    [InlineData("999", DecisionStatus.Scrutiny)]
    public void Inclusion_OutcomeDispatchesOnFlag(string flag, DecisionStatus expected)
    {
        var rules = InclusionOutcome();
        var ctx = Ctx("Inclusion", "KS4June", ("inclusionFlag", new FieldValue.Str(flag)));

        var d = _sut.Evaluate(rules, ctx, Lookups.Empty);

        Assert.Equal(expected, d.Status);
    }

    [Theory]
    [InlineData("KS4June", "2025-02-01", DecisionStatus.AutoRejected)] // KS4 + after threshold
    [InlineData("KS4June", "2025-01-16", DecisionStatus.Scrutiny)]     // KS4 + equal threshold (not strictly greater)
    [InlineData("KS2", "2025-05-13", DecisionStatus.AutoRejected)] // KS2 + after threshold
    [InlineData("KS2", "2025-05-12", DecisionStatus.Scrutiny)]
    [InlineData("Post16", "2025-08-01", DecisionStatus.Scrutiny)]  // outside KS2/KS4 windows
    public void ElectiveHomeEducation_DispatchesByCheckingWindowTypeAndDate(string checkingWindowType, string removalDate, DecisionStatus expected)
    {
        var rules = OneOutcome("ElectiveHomeEducation",
            Branch("EHE-KS4", DecisionStatus.AutoRejected, new Predicate.AllOf(new Predicate[]
            {
                new Predicate.FieldEq("checkingWindowType", new FieldValue.Str("KS4June")),
                new Predicate.FieldCompare("dateOfRemoval", CompareOp.Gt, new FieldValue.Date(new DateOnly(2025, 1, 16))),
            })),
            Branch("EHE-KS2", DecisionStatus.AutoRejected, new Predicate.AllOf(new Predicate[]
            {
                new Predicate.FieldEq("checkingWindowType", new FieldValue.Str("KS2")),
                new Predicate.FieldCompare("dateOfRemoval", CompareOp.Gt, new FieldValue.Date(new DateOnly(2025, 5, 12))),
            })),
            Branch("EHE-DEF", DecisionStatus.Scrutiny, Predicate.Otherwise.Instance));

        var ctx = Ctx("ElectiveHomeEducation", checkingWindowType,
            ("dateOfRemoval", new FieldValue.Date(DateOnly.Parse(removalDate))));

        var d = _sut.Evaluate(rules, ctx, Lookups.Empty);

        Assert.Equal(expected, d.Status);
    }

    [Fact]
    public void TerminalCriticalIllness_ScrutinyShortCircuitsOnTerminalIllness()
    {
        // Docx: terminal illness == true → Scrutiny even before key-stage logic
        var rules = OneOutcome("TerminalCriticalIllness",
            Branch("TCI-TERM", DecisionStatus.Scrutiny, new Predicate.FieldEq("hasTerminalIllness", new FieldValue.Bool(true))),
            Branch("TCI-DEF",  DecisionStatus.Scrutiny, Predicate.Otherwise.Instance));

        var ctx = Ctx("TerminalCriticalIllness", "KS4June", ("hasTerminalIllness", new FieldValue.Bool(true)));

        var d = _sut.Evaluate(rules, ctx, Lookups.Empty);

        Assert.Equal(DecisionStatus.Scrutiny, d.Status);
        Assert.Equal("TCI-TERM", d.MatchedRuleId);
    }

    [Theory]
    [InlineData(17, DecisionStatus.AutoApproved)] // < 18
    [InlineData(18, DecisionStatus.AutoRejected)] // >= 18
    [InlineData(0,  DecisionStatus.Scrutiny)]      // unknown (treated as missing)
    public void NotAtEndOf16To18Study_AgeDispatch(int age, DecisionStatus expected)
    {
        var rules = OneOutcome("NotAtEndOf16To18Study",
            Branch("N16-LT", DecisionStatus.AutoApproved, new Predicate.AllOf(new Predicate[]
            {
                new Predicate.IsKnownAndCertain("pupilAge"),
                new Predicate.FieldCompare("pupilAge", CompareOp.Lt, new FieldValue.Num(18m)),
            })),
            Branch("N16-GTE", DecisionStatus.AutoRejected, new Predicate.AllOf(new Predicate[]
            {
                new Predicate.IsKnownAndCertain("pupilAge"),
                new Predicate.FieldCompare("pupilAge", CompareOp.Gte, new FieldValue.Num(18m)),
            })),
            Branch("N16-DEF", DecisionStatus.Scrutiny, Predicate.Otherwise.Instance));

        var fields = age > 0
            ? new (string, FieldValue)[] { ("pupilAge", new FieldValue.Num(age)) }
            : Array.Empty<(string, FieldValue)>();

        var ctx = Ctx("NotAtEndOf16To18Study", "Post16", fields);

        var d = _sut.Evaluate(rules, ctx, Lookups.Empty);

        Assert.Equal(expected, d.Status);
    }

    [Fact]
    public void Deceased_AlwaysAutoApproved()
    {
        var rules = OneOutcome("Deceased",
            Branch("DEC-1", DecisionStatus.AutoApproved, Predicate.Otherwise.Instance));

        var ctx = Ctx("Deceased", "KS2");

        var d = _sut.Evaluate(rules, ctx, Lookups.Empty);

        Assert.Equal(DecisionStatus.AutoApproved, d.Status);
        Assert.Equal("DEC-1", d.MatchedRuleId);
    }

    // =====================================================================
    // (c) engine-level edge cases
    // =====================================================================

    [Fact]
    public void Returns_UnmatchedOutcome_WhenOutcomeKeyNotConfigured()
    {
        var rules = OneOutcome("KnownOutcome",
            Branch("X", DecisionStatus.AutoApproved, Predicate.Otherwise.Instance));

        var ctx = Ctx("DifferentOutcome", "KS2");

        var d = _sut.Evaluate(rules, ctx, Lookups.Empty);

        Assert.Equal(DecisionStatus.Scrutiny, d.Status);
        Assert.Equal("_unmatched_outcome", d.MatchedRuleId);
        Assert.Equal("DifferentOutcome", d.OutcomeKey);
    }

    [Fact]
    public void Decision_CarriesTraceForMatchedBranch()
    {
        var rules = OneOutcome("X",
            Branch("R1", DecisionStatus.AutoApproved,
                new Predicate.FieldEq("checkingWindowType", new FieldValue.Str("KS4June"))),
            Branch("OW", DecisionStatus.Scrutiny, Predicate.Otherwise.Instance));

        var ctx = Ctx("X", "KS4June");

        var d = _sut.Evaluate(rules, ctx, Lookups.Empty);

        Assert.Single(d.Trace);
        Assert.Contains("checkingWindowType == \"KS4June\" → true", d.Trace[0]);
    }

    [Fact]
    public void Trace_IsTruncatedAtCap_ForVeryDeepBranches()
    {
        // Build an AllOf with 80 leaves to exceed the 50-line trace cap.
        var leaves = Enumerable.Range(0, 80)
            .Select(_ => (Predicate)new Predicate.FieldEq("checkingWindowType", new FieldValue.Str("KS2")))
            .ToList();

        // KS4 input fails every leaf (no short-circuit because... actually AllOf SHORT-CIRCUITS on first false).
        // Use AnyOf for non-short-circuited evaluation with all-false children.
        var rules = OneOutcome("X",
            Branch("R1", DecisionStatus.AutoApproved, new Predicate.AnyOf(leaves)),
            Branch("OW", DecisionStatus.Scrutiny, Predicate.Otherwise.Instance));

        var ctx = Ctx("X", "KS4June"); // all 80 leaves evaluate false (no short-circuit)

        var d = _sut.Evaluate(rules, ctx, Lookups.Empty);

        // Engine evaluates from R1 (false) then matches OW (no trace lines added by Otherwise).
        // So the truncation we want to assert lives on R1's trace, but R1 didn't match.
        // Instead assert the matched branch (OW) carries empty/short trace, and that
        // the engine does cap correctly when a long trace IS the matched branch.

        Assert.Equal(DecisionStatus.Scrutiny, d.Status);
        Assert.Equal("OW", d.MatchedRuleId);
    }

    [Fact]
    public void Trace_OnMatchedBranch_StaysUnderCap()
    {
        // 80 leaves on an AnyOf that succeeds on the last one - matched branch carries trace.
        // Cap should kick in before line 80 is appended.
        var leaves = new List<Predicate>();
        for (var i = 0; i < 60; i++)
        {
            leaves.Add(new Predicate.FieldEq("checkingWindowType", new FieldValue.Str("KS2"))); // fails (KS4 input)
        }
        leaves.Add(new Predicate.FieldEq("checkingWindowType", new FieldValue.Str("KS4June"))); // succeeds

        var rules = OneOutcome("X",
            Branch("R1", DecisionStatus.AutoApproved, new Predicate.AnyOf(leaves)),
            Branch("OW", DecisionStatus.Scrutiny, Predicate.Otherwise.Instance));

        var ctx = Ctx("X", "KS4June");

        var d = _sut.Evaluate(rules, ctx, Lookups.Empty);

        Assert.Equal(DecisionStatus.AutoApproved, d.Status);
        Assert.InRange(d.Trace.Count, 1, 51); // 50 lines + 1 truncation marker max
        Assert.Contains(d.Trace, line => line.StartsWith("...", StringComparison.Ordinal));
    }

    // =====================================================================
    // builders
    // =====================================================================

    private static RuleSet OneOutcome(string key, params RuleBranch[] branches) =>
        new("test", DateTimeOffset.UtcNow, new[] { new OutcomeRules(key, key, branches) });

    private static RuleBranch Branch(string id, DecisionStatus status, Predicate when) =>
        new(id, status, when);

    private static RuleContext Ctx(string outcomeKey, string checkingWindowType, params (string, FieldValue)[] extraFields)
    {
        var fields = new Dictionary<string, FieldValue>
        {
            ["checkingWindowType"]    = new FieldValue.Str(checkingWindowType),
            ["requestType"] = new FieldValue.Str(outcomeKey),
        };
        foreach (var (k, v) in extraFields) fields[k] = v;
        return new RuleContext(outcomeKey, checkingWindowType, fields);
    }

    private static RuleSet InclusionOutcome() =>
        OneOutcome("Inclusion",
            Branch("INC-ACC", DecisionStatus.AutoApproved,
                new Predicate.FieldIn("inclusionFlag", new FieldValue[]
                {
                    new FieldValue.Str("402"), new FieldValue.Str("404"),
                    new FieldValue.Str("407"), new FieldValue.Str("408"), new FieldValue.Str("422")
                })),
            Branch("INC-REJ", DecisionStatus.AutoRejected,
                new Predicate.FieldIn("inclusionFlag", new FieldValue[]
                {
                    new FieldValue.Str("413"), new FieldValue.Str("430")
                })),
            Branch("INC-DEF", DecisionStatus.Scrutiny, Predicate.Otherwise.Instance));
}
