using System.Text.Json;
using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Application.RulesEngine.Json;
using RulesEngineImpl = DfE.CheckPerformanceData.Application.RulesEngine.RulesEngine;

namespace DfE.CheckPerformanceData.Application.UnitTests.RulesEngine;

/// <summary>
/// AB#304117: the seed's PupilMissingInEducation outcome auto-rejects a "Child missing
/// education" removal whose why-removed answer is Other, and sends Ground H / Ground I to
/// Scrutiny. Drives the real engine over the shipped seed rules.json with a hand-built
/// context, so it runs without Postgres; RulesEngineEndToEndTests covers the same branches
/// through the consumer and the mapper.
///
/// A missing answer must also fall to Scrutiny: the field is Unknown, PMIE-OTHER-REJ does not
/// match, and the terminal otherwise branch wins — "always Scrutiny on doubt".
/// </summary>
public sealed class PupilMissingInEducationRulesTests
{
    private const string OutcomeKey = "PupilMissingInEducation";
    private const string Field = "childMissingEducationGround";

    [Fact]
    public void Other_IsAutoRejected_ByPmieOtherRej()
    {
        var decision = Evaluate(Field, new FieldValue.Str("other"));

        Assert.Equal(DecisionStatus.AutoRejected, decision.Status);
        Assert.Equal("PMIE-OTHER-REJ", decision.MatchedRuleId);
    }

    [Theory]
    [InlineData("not-returned-after-agreed-leave")] // Ground H
    [InlineData("no-agreed-leave-or-reason")]       // Ground I
    public void GroundHOrGroundI_FallsToScrutiny(string ground)
    {
        var decision = Evaluate(Field, new FieldValue.Str(ground));

        Assert.Equal(DecisionStatus.Scrutiny, decision.Status);
        Assert.Equal("PMIE-DEF", decision.MatchedRuleId);
    }

    [Fact]
    public void NoAnswer_FallsToScrutiny()
    {
        var decision = Evaluate(field: null, value: null);

        Assert.Equal(DecisionStatus.Scrutiny, decision.Status);
        Assert.Equal("PMIE-DEF", decision.MatchedRuleId);
    }

    // --- helpers ---

    private static Decision Evaluate(string? field, FieldValue? value)
    {
        var fields = new Dictionary<string, FieldValue>
        {
            ["checkingWindowType"] = new FieldValue.Str("KS4June"),
            ["requestType"]        = new FieldValue.Str("Remove - child-missing-education"),
        };
        if (field is not null && value is not null) fields[field] = value;

        var ctx = new RuleContext(OutcomeKey, "KS4June", fields);
        return new RulesEngineImpl().Evaluate(LoadSeedRules(), ctx, Lookups.Empty);
    }

    private static RuleSet LoadSeedRules()
    {
        var json = File.ReadAllText(Path.Combine(LocateSeedDirectory(), "rules.json"));
        var parsed = JsonSerializer.Deserialize<RuleSet>(json, RulesJson.Options)!;
        var validation = new RuleSetValidator().Validate(parsed);
        Assert.True(validation.IsValid, string.Join("\n  ", validation.Errors));
        return validation.ResolvedRules!;
    }

    private static string LocateSeedDirectory()
    {
        var relative = Path.Combine("src", "DfE.CheckPerformanceData.RulesEngineWorker", "seed");
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException($"Could not locate {relative} from {AppContext.BaseDirectory}.");
    }
}
