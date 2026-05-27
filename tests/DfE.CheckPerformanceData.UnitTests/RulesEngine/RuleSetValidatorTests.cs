using DfE.CheckPerformanceData.Application.RulesEngine;

namespace DfE.CheckPerformanceData.Application.UnitTests.RulesEngine;

public sealed class RuleSetValidatorTests
{
    private readonly RuleSetValidator _sut = new();

    // --- schema-level ---

    [Fact]
    public void Fails_WhenRuleSetIsNull()
    {
        var result = _sut.Validate(null!);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("null", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Fails_WhenOutcomesListIsEmpty()
    {
        var rules = MakeRuleSet(Array.Empty<OutcomeRules>());

        var result = _sut.Validate(rules);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Outcomes is empty"));
    }

    [Fact]
    public void Fails_WhenOutcomeHasNoRules()
    {
        var rules = MakeRuleSet(new[] { new OutcomeRules("Deceased", "Deceased", Array.Empty<RuleBranch>()) });

        var result = _sut.Validate(rules);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("'Deceased' has no rules"));
    }

    [Fact]
    public void Fails_WhenFinalBranchIsNotOtherwise()
    {
        var rules = MakeRuleSet(new[]
        {
            Outcome("Deceased",
                Branch("D1", DecisionStatus.AutoApproved, new Predicate.FieldEq("keyStage", new FieldValue.Str("KS2"))))
        });

        var result = _sut.Validate(rules);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("final branch") && e.Contains("must be 'otherwise'"));
    }

    [Fact]
    public void Fails_WhenOtherwiseAppearsBeforeLastBranch()
    {
        var rules = MakeRuleSet(new[]
        {
            Outcome("Deceased",
                Branch("D1", DecisionStatus.Scrutiny, Predicate.Otherwise.Instance),
                Branch("D2", DecisionStatus.AutoApproved, new Predicate.FieldEq("keyStage", new FieldValue.Str("KS2"))),
                Branch("D3", DecisionStatus.Scrutiny, Predicate.Otherwise.Instance))
        });

        var result = _sut.Validate(rules);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("'D1'") && e.Contains("before the last position"));
    }

    [Fact]
    public void Fails_WhenBranchIdsAreNotUnique()
    {
        var rules = MakeRuleSet(new[]
        {
            Outcome("EHE",
                Branch("DUP", DecisionStatus.AutoRejected, new Predicate.FieldEq("keyStage", new FieldValue.Str("KS4"))),
                Branch("DUP", DecisionStatus.Scrutiny, Predicate.Otherwise.Instance))
        });

        var result = _sut.Validate(rules);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("duplicate branch id 'DUP'"));
    }

    // --- field validation ---

    [Fact]
    public void Fails_WhenFieldIsNotInCatalogue()
    {
        var rules = MakeRuleSet(new[]
        {
            Outcome("EHE",
                Branch("B1", DecisionStatus.AutoRejected, new Predicate.FieldEq("notAField", new FieldValue.Str("x"))),
                Branch("B2", DecisionStatus.Scrutiny, Predicate.Otherwise.Instance))
        });

        var result = _sut.Validate(rules);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("unknown field 'notAField'"));
    }

    [Fact]
    public void Fails_WhenComparingOnNonComparableField()
    {
        // isAddBack is Bool; lt/lte/gt/gte don't make sense.
        var rules = MakeRuleSet(new[]
        {
            Outcome("EAL",
                Branch("B1", DecisionStatus.AutoRejected,
                    new Predicate.FieldCompare("isAddBack", CompareOp.Lt, new FieldValue.Bool(true))),
                Branch("B2", DecisionStatus.Scrutiny, Predicate.Otherwise.Instance))
        });

        var result = _sut.Validate(rules);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("cannot use lt/lte/gt/gte on 'isAddBack'"));
    }

    [Fact]
    public void Fails_WhenInListIsEmpty()
    {
        var rules = MakeRuleSet(new[]
        {
            Outcome("Inclusion",
                Branch("B1", DecisionStatus.AutoApproved, new Predicate.FieldIn("inclusionFlag", Array.Empty<FieldValue>())),
                Branch("B2", DecisionStatus.Scrutiny, Predicate.Otherwise.Instance))
        });

        var result = _sut.Validate(rules);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("empty 'in' list"));
    }

    // --- literal coercion ---

    [Fact]
    public void CoercesNumericLiteralToStringForStringField()
    {
        // inclusionFlag is String; business users may write "in": [402, 404]
        var rules = MakeRuleSet(new[]
        {
            Outcome("Inclusion",
                Branch("B1", DecisionStatus.AutoApproved, new Predicate.FieldIn("inclusionFlag",
                    new FieldValue[] { new FieldValue.Num(402m), new FieldValue.Num(404m) })),
                Branch("B2", DecisionStatus.Scrutiny, Predicate.Otherwise.Instance))
        });

        var result = _sut.Validate(rules);

        Assert.True(result.IsValid, "Expected validation to succeed: " + string.Join("; ", result.Errors));
        var resolved = (Predicate.FieldIn)result.ResolvedRules!.Outcomes[0].Rules[0].When;
        Assert.Equal(new FieldValue.Str("402"), resolved.Values[0]);
        Assert.Equal(new FieldValue.Str("404"), resolved.Values[1]);
    }

    [Fact]
    public void CoercesIsoStringLiteralToDateForDateField()
    {
        var rules = MakeRuleSet(new[]
        {
            Outcome("EHE",
                Branch("B1", DecisionStatus.AutoRejected,
                    new Predicate.FieldCompare("dateOfRemoval", CompareOp.Gt, new FieldValue.Str("2025-01-16"))),
                Branch("B2", DecisionStatus.Scrutiny, Predicate.Otherwise.Instance))
        });

        var result = _sut.Validate(rules);

        Assert.True(result.IsValid, "Expected validation to succeed: " + string.Join("; ", result.Errors));
        var resolved = (Predicate.FieldCompare)result.ResolvedRules!.Outcomes[0].Rules[0].When;
        var dateLiteral = Assert.IsType<FieldValue.Date>(resolved.Value);
        Assert.Equal(new DateOnly(2025, 1, 16), dateLiteral.Value);
    }

    [Fact]
    public void Fails_WhenDateFieldHasNonIsoString()
    {
        var rules = MakeRuleSet(new[]
        {
            Outcome("EHE",
                Branch("B1", DecisionStatus.AutoRejected,
                    new Predicate.FieldCompare("dateOfRemoval", CompareOp.Gt, new FieldValue.Str("16/01/2025"))),
                Branch("B2", DecisionStatus.Scrutiny, Predicate.Otherwise.Instance))
        });

        var result = _sut.Validate(rules);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("dateOfRemoval") && e.Contains("ISO yyyy-MM-dd"));
    }

    [Fact]
    public void Fails_WhenBoolFieldHasStringLiteral()
    {
        var rules = MakeRuleSet(new[]
        {
            Outcome("EAL",
                Branch("B1", DecisionStatus.AutoApproved,
                    new Predicate.FieldEq("isAddBack", new FieldValue.Str("yes"))),
                Branch("B2", DecisionStatus.Scrutiny, Predicate.Otherwise.Instance))
        });

        var result = _sut.Validate(rules);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Bool field 'isAddBack'"));
    }

    [Fact]
    public void Succeeds_OnMinimalWellFormedRuleSet()
    {
        var rules = MakeRuleSet(new[]
        {
            Outcome("Deceased",
                Branch("D1", DecisionStatus.AutoApproved, Predicate.Otherwise.Instance))
        });

        var result = _sut.Validate(rules);

        Assert.True(result.IsValid);
        Assert.NotNull(result.ResolvedRules);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void RecursesIntoNestedCombinators()
    {
        // Unknown field nested inside any/all should still surface.
        var rules = MakeRuleSet(new[]
        {
            Outcome("EAL",
                Branch("B1", DecisionStatus.AutoApproved,
                    new Predicate.AllOf(new Predicate[]
                    {
                        new Predicate.FieldEq("firstLanguage", new FieldValue.Str("OTH")),
                        new Predicate.AnyOf(new Predicate[]
                        {
                            new Predicate.FieldEq("notAField", new FieldValue.Str("x")),
                        })
                    })),
                Branch("B2", DecisionStatus.Scrutiny, Predicate.Otherwise.Instance))
        });

        var result = _sut.Validate(rules);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("unknown field 'notAField'"));
    }

    // --- helpers ---

    private static RuleSet MakeRuleSet(IEnumerable<OutcomeRules> outcomes) =>
        new("test-version", DateTimeOffset.UtcNow, outcomes.ToList());

    private static OutcomeRules Outcome(string key, params RuleBranch[] branches) =>
        new(key, key, branches);

    private static RuleBranch Branch(string id, DecisionStatus status, Predicate when) =>
        new(id, status, when);
}
