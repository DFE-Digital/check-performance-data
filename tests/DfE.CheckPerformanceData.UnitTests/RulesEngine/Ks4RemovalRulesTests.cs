using System.Text.Json;
using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Application.RulesEngine.Json;
using RulesEngineImpl = DfE.CheckPerformanceData.Application.RulesEngine.RulesEngine;

namespace DfE.CheckPerformanceData.Application.UnitTests.RulesEngine;

/// <summary>
/// The shipped KS4 branches were converted from the fixed dates of the 2026 window:
/// the EAL new-admission cutoff was 1 September 2022 (1 September four years before the
/// window year — the same boundary the journey validates as <c>cohort.start</c>), the
/// add-back cutoff 1 September 2021 (five years back), and the EHE/PEX cutoff 16 January
/// of the window year. The EAL offsets reproduce the 2026 fixed dates exactly; the
/// EHE/PEX fixed date of 16 January 2025 rolled forward to 16 January of the window year
/// at conversion, by agreement. These tests run the real engine over the shipped seed
/// rules.json with a 2026 window start and pin each converted branch, so a change to an
/// offset in rules.json fails here. In any other window year the dates move by design
/// (see seed/README.md); a 2027 window test pins the moved boundary itself.
/// </summary>
public sealed class Ks4RemovalRulesTests
{
    private static readonly DateOnly WindowStart = new(2026, 1, 1);

    // The converted branches' original fixed dates for the 2026 window.
    private static readonly DateOnly NewAdmissionCutoffBefore = new(2022, 8, 31);
    private static readonly DateOnly NewAdmissionCutoffOn = new(2022, 9, 1);
    private static readonly DateOnly AddBackCutoffBefore = new(2021, 8, 31);
    private static readonly DateOnly AddBackCutoffOn = new(2021, 9, 1);
    private static readonly DateOnly JanuaryCutoffOn = new(2026, 1, 16);
    private static readonly DateOnly JanuaryCutoffAfter = new(2026, 1, 17);

    // --- Admitted from abroad with English not first language ---

    [Fact]
    public void NewAdmission_BothAdmissionsOnCutoff_IsAutoApproved()
    {
        var decision = EvaluateEal(
            schoolAdmission: NewAdmissionCutoffOn,
            firstAdmission: NewAdmissionCutoffOn,
            addBack: false,
            arrival: null);

        Assert.Equal(DecisionStatus.AutoApproved, decision.Status);
        Assert.Equal("EAL-ACC-NEWADM-2022-ALL", decision.MatchedRuleId);
    }

    [Fact]
    public void NewAdmission_BothAdmissionsBeforeCutoff_GoesToScrutiny()
    {
        var decision = EvaluateEal(
            schoolAdmission: NewAdmissionCutoffBefore,
            firstAdmission: NewAdmissionCutoffBefore,
            addBack: false,
            arrival: null);

        Assert.Equal(DecisionStatus.Scrutiny, decision.Status);
        Assert.Equal("EAL-DEF", decision.MatchedRuleId);
    }

    [Fact]
    public void NewAdmission_FirstAdmissionBeforeCutoff_ArrivalOnCutoff_IsAutoApproved()
    {
        // The ARRIVAL branch: the current school admission and the arrival are on or after
        // the cutoff, while the first-ever admission is not.
        var decision = EvaluateEal(
            schoolAdmission: NewAdmissionCutoffOn,
            firstAdmission: NewAdmissionCutoffBefore,
            addBack: false,
            arrival: NewAdmissionCutoffOn);

        Assert.Equal(DecisionStatus.AutoApproved, decision.Status);
        Assert.Equal("EAL-ACC-NEWADM-2022-ARRIVAL", decision.MatchedRuleId);
    }

    [Fact]
    public void AddBack_BothAdmissionsOnCutoff_IsAutoApproved()
    {
        var decision = EvaluateEal(
            schoolAdmission: AddBackCutoffOn,
            firstAdmission: AddBackCutoffOn,
            addBack: true,
            arrival: null);

        Assert.Equal(DecisionStatus.AutoApproved, decision.Status);
        Assert.Equal("EAL-ACC-ADDBACK-2021-ALL", decision.MatchedRuleId);
    }

    [Fact]
    public void AddBack_BothAdmissionsBeforeCutoff_GoesToScrutiny()
    {
        var decision = EvaluateEal(
            schoolAdmission: AddBackCutoffBefore,
            firstAdmission: AddBackCutoffBefore,
            addBack: true,
            arrival: null);

        Assert.Equal(DecisionStatus.Scrutiny, decision.Status);
        Assert.Equal("EAL-DEF", decision.MatchedRuleId);
    }

    // --- Elective home education ---

    [Fact]
    public void Ehe_RemovalAfterJanuaryCutoff_IsAutoRejected()
    {
        var decision = EvaluateJanuaryCutoff("ElectiveHomeEducation", "dateOfRemoval", JanuaryCutoffAfter);

        Assert.Equal(DecisionStatus.AutoRejected, decision.Status);
        Assert.Equal("EHE-KS4", decision.MatchedRuleId);
    }

    [Fact]
    public void Ehe_RemovalOnJanuaryCutoff_GoesToScrutiny()
    {
        // gt is strict: the cutoff day itself does not reject.
        var decision = EvaluateJanuaryCutoff("ElectiveHomeEducation", "dateOfRemoval", JanuaryCutoffOn);

        Assert.Equal(DecisionStatus.Scrutiny, decision.Status);
        Assert.Equal("EHE-DEF", decision.MatchedRuleId);
    }

    // --- Permanently excluded from current school ---

    [Fact]
    public void Pex_ExclusionAfterJanuaryCutoff_IsAutoRejected()
    {
        var decision = EvaluateJanuaryCutoff("PermanentlyExcludedFromCurrentSchool", "dateOfPermanentExclusion", JanuaryCutoffAfter);

        Assert.Equal(DecisionStatus.AutoRejected, decision.Status);
        Assert.Equal("PEX-KS4", decision.MatchedRuleId);
    }

    [Fact]
    public void Pex_ExclusionOnJanuaryCutoff_GoesToScrutiny()
    {
        var decision = EvaluateJanuaryCutoff("PermanentlyExcludedFromCurrentSchool", "dateOfPermanentExclusion", JanuaryCutoffOn);

        Assert.Equal(DecisionStatus.Scrutiny, decision.Status);
        Assert.Equal("PEX-DEF", decision.MatchedRuleId);
    }

    // --- window-year dependence: the same branches take a different cutoff in a different window ---

    [Fact]
    public void NewAdmission_BoundaryMovesWithTheWindowYear()
    {
        // The 2022-09-01 boundary is the 2026 window's cohort start. A 2027 window moves it
        // to 1 September 2023: a pupil admitted on 2022-09-01, auto-approved in the 2026
        // window, goes to Scrutiny in the 2027 window, and 1 September 2023 is the new
        // approval boundary.
        var admittedOnOldBoundary = new DateOnly(2022, 9, 1);
        var movedBoundary = new DateOnly(2023, 9, 1);
        var olderWindow = new DateOnly(2027, 1, 1);

        var moved = EvaluateEal(admittedOnOldBoundary, admittedOnOldBoundary, addBack: false,
            arrival: null, windowStart: olderWindow);

        Assert.Equal(DecisionStatus.Scrutiny, moved.Status);
        Assert.Equal("EAL-DEF", moved.MatchedRuleId);

        var atNewBoundary = EvaluateEal(movedBoundary, movedBoundary, addBack: false,
            arrival: null, windowStart: olderWindow);

        Assert.Equal(DecisionStatus.AutoApproved, atNewBoundary.Status);
        Assert.Equal("EAL-ACC-NEWADM-2022-ALL", atNewBoundary.MatchedRuleId);
    }

    [Fact]
    public void JanuaryCutoff_MovesWithTheWindowYear()
    {
        // EHE/PEX reject strictly after 16 January of the window year. In a 2027 window the
        // 2026 boundary no longer rejects.
        var olderWindow = new DateOnly(2027, 1, 1);

        var atOldBoundary = EvaluateJanuaryCutoff("ElectiveHomeEducation", "dateOfRemoval",
            JanuaryCutoffAfter, windowStart: olderWindow);

        Assert.Equal(DecisionStatus.Scrutiny, atOldBoundary.Status);
        Assert.Equal("EHE-DEF", atOldBoundary.MatchedRuleId);

        var afterNewBoundary = EvaluateJanuaryCutoff("ElectiveHomeEducation", "dateOfRemoval",
            new(2027, 1, 17), windowStart: olderWindow);

        Assert.Equal(DecisionStatus.AutoRejected, afterNewBoundary.Status);
        Assert.Equal("EHE-KS4", afterNewBoundary.MatchedRuleId);
    }

    // --- helpers ---

    private static Decision EvaluateEal(
        DateOnly? schoolAdmission, DateOnly? firstAdmission, bool addBack, DateOnly? arrival,
        DateOnly? windowStart = null)
    {
        var fields = Ks4Fields("Remove - english-not-first-language");
        fields["firstLanguage"] = new FieldValue.Str("OTH");
        fields["countryOfOrigin"] = new FieldValue.Str("PL");
        fields["isAddBack"] = new FieldValue.Bool(addBack);
        if (schoolAdmission is { } s) fields["schoolAdmissionDate"] = new FieldValue.Date(s);
        if (firstAdmission is { } f) fields["firstSchoolAdmissionDate"] = new FieldValue.Date(f);
        if (arrival is { } a) fields["dateOfArrivalInEngland"] = new FieldValue.Date(a);

        return Evaluate("AdmittedFromAbroadEal", fields, windowStart ?? WindowStart);
    }

    private static Decision EvaluateJanuaryCutoff(string outcomeKey, string field, DateOnly date,
        DateOnly? windowStart = null)
    {
        var fields = Ks4Fields("Remove");
        fields[field] = new FieldValue.Date(date);

        return Evaluate(outcomeKey, fields, windowStart ?? WindowStart);
    }

    private static Dictionary<string, FieldValue> Ks4Fields(string requestType) => new()
    {
        ["checkingWindowType"] = new FieldValue.Str("KS4June"),
        ["requestType"] = new FieldValue.Str(requestType),
    };

    private static Decision Evaluate(string outcomeKey, Dictionary<string, FieldValue> fields, DateOnly windowStart) =>
        new RulesEngineImpl().Evaluate(LoadSeedRules(), new RuleContext(outcomeKey, "KS4June", fields, windowStart), Lookups.Empty);

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
