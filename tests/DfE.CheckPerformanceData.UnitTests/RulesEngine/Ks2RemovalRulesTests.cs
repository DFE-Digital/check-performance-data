using System.Text.Json;
using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Application.RulesEngine.Json;
using RulesEngineImpl = DfE.CheckPerformanceData.Application.RulesEngine.RulesEngine;

namespace DfE.CheckPerformanceData.Application.UnitTests.RulesEngine;

/// <summary>
/// #813: KS2 September removal rules that the business answers changed. Runs the real engine
/// over the shipped seed rules.json with a hand-built context.
/// </summary>
public sealed class Ks2RemovalRulesTests
{
    private static readonly DateOnly BeforeCutoff = new(2024, 8, 31);
    private static readonly DateOnly OnCutoff = new(2024, 9, 1);

    // --- Admitted from abroad with English not first language ---

    [Theory]
    [InlineData(true, false)]  // admitted to the current school before 1 September
    [InlineData(false, true)]  // first admitted to a school in England before 1 September
    public void Eal_AdmissionBeforeCutoff_WithNoEvidence_IsAutoRejected(bool schoolBefore, bool firstBefore)
    {
        var decision = EvaluateEal(schoolBefore ? BeforeCutoff : OnCutoff, firstBefore ? BeforeCutoff : OnCutoff, evidence: false);

        Assert.Equal(DecisionStatus.AutoRejected, decision.Status);
        Assert.Equal("EAL-REJ-KS2-NOEVIDENCE", decision.MatchedRuleId);
    }

    [Fact]
    public void Eal_AdmissionBeforeCutoff_WithEvidence_GoesToScrutiny()
    {
        var decision = EvaluateEal(BeforeCutoff, BeforeCutoff, evidence: true);

        Assert.Equal(DecisionStatus.Scrutiny, decision.Status);
        Assert.Equal("EAL-DEF", decision.MatchedRuleId);
    }

    [Fact]
    public void Eal_AdmissionBeforeCutoff_WithEvidenceUnknown_GoesToScrutiny()
    {
        var decision = EvaluateEal(BeforeCutoff, BeforeCutoff, evidence: null);

        Assert.Equal(DecisionStatus.Scrutiny, decision.Status);
    }

    [Fact]
    public void Eal_BothAdmissionsOnOrAfterCutoff_WithNoEvidence_IsAutoApproved()
    {
        var decision = EvaluateEal(OnCutoff, OnCutoff, evidence: false);

        Assert.Equal(DecisionStatus.AutoApproved, decision.Status);
        Assert.Equal("EAL-ACC-KS2-ALL", decision.MatchedRuleId);
    }

    // --- Terminal/Critical illness ---

    [Fact]
    public void Illness_SatTestsAsYear6_NotTerminal_IsAutoRejected()
    {
        var decision = EvaluateIllness(terminal: false, satTests: true);

        Assert.Equal(DecisionStatus.AutoRejected, decision.Status);
        Assert.Equal("TCI-KS2-REJ", decision.MatchedRuleId);
    }

    [Fact]
    public void Illness_DidNotSitTests_GoesToScrutiny()
    {
        var decision = EvaluateIllness(terminal: false, satTests: false);

        Assert.Equal(DecisionStatus.Scrutiny, decision.Status);
        Assert.Equal("TCI-DEF", decision.MatchedRuleId);
    }

    [Fact]
    public void Illness_Terminal_SatTests_GoesToScrutiny()
    {
        var decision = EvaluateIllness(terminal: true, satTests: true);

        Assert.Equal(DecisionStatus.Scrutiny, decision.Status);
        Assert.Equal("TCI-TERM-SCR", decision.MatchedRuleId);
    }

    // --- helpers ---

    private static Decision EvaluateEal(DateOnly schoolAdmission, DateOnly firstAdmission, bool? evidence)
    {
        var fields = Ks2Fields("Remove - english-not-first-language");
        fields["firstLanguage"] = new FieldValue.Str("OTH");
        fields["countryOfOrigin"] = new FieldValue.Str("PL");
        fields["schoolAdmissionDate"] = new FieldValue.Date(schoolAdmission);
        fields["firstSchoolAdmissionDate"] = new FieldValue.Date(firstAdmission);
        if (evidence is { } e) fields["hasSupportingEvidence"] = new FieldValue.Bool(e);

        return Evaluate("AdmittedFromAbroadEal", fields);
    }

    private static Decision EvaluateIllness(bool terminal, bool satTests)
    {
        var fields = Ks2Fields("Remove - life-limiting-illness");
        fields["hasTerminalIllness"] = new FieldValue.Bool(terminal);
        fields["hasSatExamsAsYear6"] = new FieldValue.Bool(satTests);

        return Evaluate("TerminalCriticalIllness", fields);
    }

    private static Dictionary<string, FieldValue> Ks2Fields(string requestType) => new()
    {
        ["checkingWindowType"] = new FieldValue.Str("KS2"),
        ["requestType"] = new FieldValue.Str(requestType),
    };

    private static Decision Evaluate(string outcomeKey, Dictionary<string, FieldValue> fields) =>
        new RulesEngineImpl().Evaluate(LoadSeedRules(), new RuleContext(outcomeKey, "KS2", fields), Lookups.Empty);

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
