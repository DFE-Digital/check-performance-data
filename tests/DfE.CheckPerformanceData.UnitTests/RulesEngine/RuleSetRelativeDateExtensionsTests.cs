using DfE.CheckPerformanceData.Application.RulesEngine;

namespace DfE.CheckPerformanceData.Application.UnitTests.RulesEngine;

public sealed class RuleSetRelativeDateExtensionsTests
{
    [Fact]
    public void True_WhenACompareCarriesARelativeDate()
    {
        var rules = Ruleset(
            new RuleBranch("B1", DecisionStatus.AutoApproved,
                new Predicate.FieldCompare("schoolAdmissionDate", CompareOp.Gte,
                    new FieldValue.RelativeDate(new RelativeDateSpec.FirstSeptemberMinusYears(4)))));

        Assert.True(rules.UsesWindowRelativeDates());
    }

    [Fact]
    public void True_WhenNestedInsideCombinators()
    {
        var rules = Ruleset(
            new RuleBranch("B1", DecisionStatus.AutoApproved,
                new Predicate.AnyOf(new Predicate[]
                {
                    new Predicate.FieldEq("checkingWindowType", new FieldValue.Str("KS4June")),
                    new Predicate.Not(new Predicate.FieldCompare("dateOfRemoval", CompareOp.Lt,
                        new FieldValue.RelativeDate(new RelativeDateSpec.SecondMondayOfMay()))),
                })));

        Assert.True(rules.UsesWindowRelativeDates());
    }

    [Fact]
    public void True_WhenNestedInsideAnAllOfAcrossOutcomes()
    {
        var rules = Ruleset(
            new RuleBranch("B1", DecisionStatus.AutoApproved, Predicate.Otherwise.Instance),
            new RuleBranch("B2", DecisionStatus.Scrutiny,
                new Predicate.AllOf(new Predicate[]
                {
                    new Predicate.FieldEq("checkingWindowType", new FieldValue.Str("KS4June")),
                    new Predicate.FieldEq("dateOfRemoval",
                        new FieldValue.RelativeDate(new RelativeDateSpec.DayMonthOfWindowYear(1, 16))),
                })));

        Assert.True(rules.UsesWindowRelativeDates());
    }

    [Fact]
    public void False_WhenOnlyFixedDates()
    {
        var rules = Ruleset(
            new RuleBranch("B1", DecisionStatus.AutoApproved,
                new Predicate.FieldCompare("dateOfRemoval", CompareOp.Gte, new FieldValue.Date(new DateOnly(2025, 1, 16)))));

        Assert.False(rules.UsesWindowRelativeDates());
    }

    private static RuleSet Ruleset(params RuleBranch[] branches) => new(
        "test-version", DateTimeOffset.UtcNow,
        new[] { new OutcomeRules("Deceased", "Deceased", branches) });
}
