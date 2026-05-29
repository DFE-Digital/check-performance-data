using DfE.CheckPerformanceData.Application.RulesEngine;
using Xunit;

namespace DfE.CheckPerformanceData.Application.UnitTests.RulesEngine;

public class RuleSetValidatorDuplicateKeyTests
{
    private static RuleBranch Otherwise(string id) =>
        new(id, DecisionStatus.Scrutiny, Predicate.Otherwise.Instance);

    private static OutcomeRules Outcome(string key) =>
        new(key, key, new[] { Otherwise($"{key}-DEF") });

    [Fact]
    public void Validate_duplicate_outcome_keys_reports_error()
    {
        var rules = new RuleSet("v1", DateTimeOffset.UnixEpoch,
            new[] { Outcome("Inclusion"), Outcome("Inclusion") });

        var result = new RuleSetValidator().Validate(rules);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("duplicate outcome key 'Inclusion'"));
    }

    [Fact]
    public void Validate_unique_outcome_keys_is_valid()
    {
        var rules = new RuleSet("v1", DateTimeOffset.UnixEpoch,
            new[] { Outcome("Inclusion"), Outcome("Deceased") });

        var result = new RuleSetValidator().Validate(rules);

        Assert.True(result.IsValid);
    }
}
