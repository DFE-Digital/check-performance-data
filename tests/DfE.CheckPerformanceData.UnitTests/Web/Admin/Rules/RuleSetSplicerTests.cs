using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Web.Admin.Rules;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Admin.Rules;

public sealed class RuleSetSplicerTests
{
    private static RuleSet Sample() => new("v1", DateTimeOffset.UnixEpoch, new[]
    {
        new OutcomeRules("EAL", "EAL", new[]
        {
            new RuleBranch("EAL-1", DecisionStatus.AutoApproved,
                new Predicate.FieldEq("keyStage", new FieldValue.Str("KS4"))),
            new RuleBranch("EAL-2", DecisionStatus.Scrutiny,
                new Predicate.FieldEq("keyStage", new FieldValue.Str("KS2"))),
            new RuleBranch("EAL-OTHER", DecisionStatus.Scrutiny, Predicate.Otherwise.Instance),
        })
    });

    [Fact]
    public void ReplaceBranch_Swaps_Matching_Id_Keeps_Position()
    {
        var updated = new RuleBranch("EAL-1", DecisionStatus.AutoRejected,
            new Predicate.IsKnownAndCertain("keyStage"));

        var result = RuleSetSplicer.ReplaceBranch(Sample(), "EAL", "EAL-1", updated);

        var branch = result.Outcomes[0].Rules[0];
        Assert.Equal(DecisionStatus.AutoRejected, branch.Status);
        Assert.IsType<Predicate.IsKnownAndCertain>(branch.When);
    }

    [Fact]
    public void InsertBranch_Adds_Before_Otherwise()
    {
        var added = new RuleBranch("EAL-3", DecisionStatus.Scrutiny,
            new Predicate.IsKnownAndCertain("keyStage"));

        var result = RuleSetSplicer.InsertBranch(Sample(), "EAL", added);

        var rules = result.Outcomes[0].Rules;
        Assert.Equal("EAL-3", rules[^2].Id);
        Assert.IsType<Predicate.Otherwise>(rules[^1].When);
    }

    [Fact]
    public void RemoveBranch_Drops_It()
    {
        var result = RuleSetSplicer.RemoveBranch(Sample(), "EAL", "EAL-1");
        Assert.DoesNotContain(result.Outcomes[0].Rules, b => b.Id == "EAL-1");
        Assert.Equal(2, result.Outcomes[0].Rules.Count);
    }

    [Fact]
    public void RemoveBranch_Refuses_Otherwise()
    {
        Assert.Throws<InvalidOperationException>(() => RuleSetSplicer.RemoveBranch(Sample(), "EAL", "EAL-OTHER"));
    }

    [Fact]
    public void MoveBranch_Up_Swaps_With_Previous()
    {
        var result = RuleSetSplicer.MoveBranch(Sample(), "EAL", "EAL-2", up: true);
        Assert.Equal("EAL-2", result.Outcomes[0].Rules[0].Id);
        Assert.Equal("EAL-1", result.Outcomes[0].Rules[1].Id);
    }

    [Fact]
    public void MoveBranch_Down_Cannot_Pass_Otherwise()
    {
        var result = RuleSetSplicer.MoveBranch(Sample(), "EAL", "EAL-2", up: false);
        Assert.Equal("EAL-2", result.Outcomes[0].Rules[1].Id);
        Assert.IsType<Predicate.Otherwise>(result.Outcomes[0].Rules[2].When);
    }

    [Fact]
    public void AddOutcome_Appends_And_Preserves_Existing()
    {
        var added = new OutcomeRules("NewOne", "New one",
            new[] { new RuleBranch("NewOne-OTHER", DecisionStatus.Scrutiny, Predicate.Otherwise.Instance) });

        var result = RuleSetSplicer.AddOutcome(Sample(), added);

        Assert.Equal(2, result.Outcomes.Count);
        Assert.Equal("EAL", result.Outcomes[0].Key);
        Assert.Equal("NewOne", result.Outcomes[1].Key);
    }

    [Fact]
    public void RemoveOutcome_Drops_Target()
    {
        var twoOutcomes = Sample() with
        {
            Outcomes = Sample().Outcomes
                .Append(new OutcomeRules("Keep", "Keep",
                    new[] { new RuleBranch("Keep-OTHER", DecisionStatus.Scrutiny, Predicate.Otherwise.Instance) }))
                .ToList()
        };

        var result = RuleSetSplicer.RemoveOutcome(twoOutcomes, "EAL");

        Assert.Single(result.Outcomes);
        Assert.Equal("Keep", result.Outcomes[0].Key);
    }

    [Fact]
    public void RemoveOutcome_Throws_For_Unknown_Key()
    {
        Assert.Throws<InvalidOperationException>(() => RuleSetSplicer.RemoveOutcome(Sample(), "Nope"));
    }
}
