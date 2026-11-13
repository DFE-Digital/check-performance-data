using System.Text.Json;
using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Application.RulesEngine.Json;

namespace DfE.CheckPerformanceData.Application.UnitTests.RulesEngine;

/// <summary>
/// Smoke tests against the seed <c>rules.json</c> and <c>country-languages.json</c>
/// that ship in the worker's <c>seed/</c> folder. These pin the seed to the
/// engine's schema so a business edit that breaks validation fails the build
/// well before it reaches the blob.
/// </summary>
public sealed class SeedRulesValidationTests
{
    private static readonly string SeedDir = LocateSeedDirectory();

    private static readonly string[] ExpectedOutcomeKeys =
    [
        "Inclusion",
        "AdmittedFollowingPermanentExclusion",
        "AdmittedFromAbroadEal",
        "CompletedKs4Elsewhere",
        "MergePupils",
        "SocialCareInvolvement",
        "TerminalCriticalIllness",
        "YearGroupChange",
        "Deceased",
        "ElectiveHomeEducation",
        "MovedSchoolDualRegistration",
        "NotOnRoll",
        "PermanentlyExcludedFromCurrentSchool",
        "PermanentlyLeftEngland",
        "PupilMissingInEducation",
        "AssessmentsDeferred",
        "PupilAddedAfterSummerTerm",
        "PupilNotOnJuneList",
        "NotAtEndOf16To18Study",
        "Other",
    ];

    [Fact]
    public void SeedRulesJson_ParsesAndValidates()
    {
        var json = File.ReadAllText(Path.Combine(SeedDir, "rules.json"));

        var parsed = JsonSerializer.Deserialize<RuleSet>(json, RulesJson.Options);
        Assert.NotNull(parsed);

        var validator = new RuleSetValidator();
        var result = validator.Validate(parsed);

        Assert.True(result.IsValid,
            "Seed rules.json failed validation:\n  " + string.Join("\n  ", result.Errors));
        Assert.NotNull(result.ResolvedRules);
    }

    [Fact]
    public void SeedRulesJson_CoversEveryDocxOutcome()
    {
        var json = File.ReadAllText(Path.Combine(SeedDir, "rules.json"));
        var parsed = JsonSerializer.Deserialize<RuleSet>(json, RulesJson.Options)!;

        var actualKeys = parsed.Outcomes.Select(o => o.Key).ToHashSet();

        foreach (var expected in ExpectedOutcomeKeys)
        {
            Assert.Contains(expected, actualKeys);
        }
    }

    /// <summary>
    /// Outcomes defined in the docx (and seeded in rules.json) whose journey flows
    /// have not been authored yet, so no WhatToChange contract string can reach them.
    /// Remove an entry from this list when its flow config gains a reason option —
    /// the alignment test (<see cref="QuestionFlowOutcomeKeyAlignmentTests"/>) then
    /// enforces the map entry from the flow side.
    /// </summary>
    private static readonly string[] PendingJourneyOutcomeKeys =
    [
    ];

    [Fact]
    public void SeedRulesJson_EveryOutcomeMapsToARecognisedWhatToChange()
    {
        // Sanity check: every outcome in the seed has a reverse-route in the
        // AnswerFieldMap (so an incoming WhatToChange could actually resolve to it),
        // except outcomes explicitly parked until their journey flow exists.
        var json = File.ReadAllText(Path.Combine(SeedDir, "rules.json"));
        var parsed = JsonSerializer.Deserialize<RuleSet>(json, RulesJson.Options)!;

        var routableKeys = AnswerFieldMap.WhatToChangeToOutcomeKey.Values.ToHashSet();

        foreach (var outcome in parsed.Outcomes.Where(o => !PendingJourneyOutcomeKeys.Contains(o.Key)))
        {
            Assert.Contains(outcome.Key, routableKeys);
        }
    }

    [Fact]
    public void PendingJourneyOutcomeKeys_AreNotAlsoRoutable()
    {
        // If a flow starts routing to one of these, it must be removed from the
        // pending list so the previous test guards it again.
        var routableKeys = AnswerFieldMap.WhatToChangeToOutcomeKey.Values.ToHashSet();

        foreach (var pending in PendingJourneyOutcomeKeys)
        {
            Assert.DoesNotContain(pending, routableKeys);
        }
    }

    [Fact]
    public void SeedCountryLanguagesJson_Parses()
    {
        var json = File.ReadAllText(Path.Combine(SeedDir, "country-languages.json"));

        var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, RulesJson.Options);
        Assert.NotNull(parsed);

        // Strip the comment key and assert there's at least one real entry.
        var realEntries = parsed.Where(kvp => !kvp.Key.StartsWith('_')).ToList();
        Assert.NotEmpty(realEntries);

        // Every real entry must be an array of non-empty strings.
        foreach (var (code, value) in realEntries)
        {
            Assert.Equal(JsonValueKind.Array, value.ValueKind);
            Assert.True(value.GetArrayLength() > 0, $"Country '{code}' has empty language list.");
            foreach (var lang in value.EnumerateArray())
            {
                Assert.Equal(JsonValueKind.String, lang.ValueKind);
                Assert.False(string.IsNullOrWhiteSpace(lang.GetString()), $"Country '{code}' has empty language.");
            }
        }
    }

    [Fact]
    public void SeedRulesJson_EvaluatesAllOutcomesEndToEnd_AgainstRepresentativeContext()
    {
        // Load seed and evaluate every outcome with a minimal context.
        // Most will fall through to their terminal "otherwise" -> Scrutiny which is fine;
        // the goal is to prove the engine can walk every branch without throwing.
        var json = File.ReadAllText(Path.Combine(SeedDir, "rules.json"));
        var parsed = JsonSerializer.Deserialize<RuleSet>(json, RulesJson.Options)!;
        var validation = new RuleSetValidator().Validate(parsed);
        Assert.True(validation.IsValid);
        var rules = validation.ResolvedRules!;

        var engine = new Application.RulesEngine.RulesEngine();
        var lookups = Lookups.Empty;

        foreach (var outcome in rules.Outcomes)
        {
            var ctx = new RuleContext(
                OutcomeKey: outcome.Key,
                CheckingWindowType: "KS4June",
                Fields: new Dictionary<string, FieldValue>
                {
                    ["checkingWindowType"] = new FieldValue.Str("KS4June"),
                    ["requestType"] = new FieldValue.Str(outcome.Key),
                });

            var decision = engine.Evaluate(rules, ctx, lookups);

            Assert.Equal(outcome.Key, decision.OutcomeKey);
            // The decision's matched-rule must be one declared on the outcome.
            var matched = outcome.Rules.SingleOrDefault(b => b.Id == decision.MatchedRuleId);
            Assert.NotNull(matched);
        }
    }

    /// <summary>
    /// The 14 branches converted from fixed dates to window-relative forms in
    /// #800 (research R2 table), each pinned to the fixed date it carried
    /// before conversion and the window year it targets.
    /// </summary>
    private static readonly (string BranchId, DateOnly ExpectedDate, int WindowYear)[] ConvertedBranches =
    [
        ("AFE-KS2",                  new DateOnly(2025, 9, 1), 2027),
        ("EAL-ACC-NEWADM-2022-ALL",  new DateOnly(2022, 9, 1), 2025),
        ("EAL-ACC-NEWADM-2022-ARRIVAL", new DateOnly(2022, 9, 1), 2025),
        ("EAL-ACC-ADDBACK-2021-ALL", new DateOnly(2021, 9, 1), 2025),
        ("EAL-ACC-ADDBACK-2021-ARRIVAL", new DateOnly(2021, 9, 1), 2025),
        ("EAL-ACC-KS2-ALL",          new DateOnly(2024, 9, 1), 2027),
        ("EAL-ACC-KS2-ARRIVAL",      new DateOnly(2024, 9, 1), 2027),
        ("EAL-REJ-KS2-NOEVIDENCE",   new DateOnly(2024, 9, 1), 2027),
        ("EHE-KS4",                  new DateOnly(2025, 1, 16), 2025),
        ("EHE-KS2",                  new DateOnly(2027, 5, 10), 2027),
        ("PEX-KS4",                  new DateOnly(2025, 1, 16), 2025),
        ("PEX-KS2",                  new DateOnly(2027, 5, 10), 2027),
        ("PLE-KS2",                  new DateOnly(2027, 5, 10), 2027),
        ("PAS-REJ",                  new DateOnly(2027, 4, 1), 2027),
    ];

    [Fact]
    public void SeedRulesJson_AtMostTwoDatedRulesStillNameACalendarYear()
    {
        // SC-001: the converted families carry window-relative forms. Only the
        // two documented outliers (AFE-PRE2023, PLE-KS4) may keep a fixed ISO
        // date. Fixed ISO date literals deserialize as FieldValue.Str until the
        // validator coerces them, so count Str literals on Date-typed fields.
        var parsed = LoadSeed();

        var fixedDates = parsed.Outcomes
            .SelectMany(o => o.Rules)
            .SelectMany(b => FixedDateLiterals(b.When))
            .ToList();

        Assert.True(fixedDates.Count <= 2,
            "SC-001: at most 2 dated rules may name a fixed calendar year; found "
            + string.Join(", ", fixedDates));
    }

    [Fact]
    public void SeedRulesJson_ConvertedBranchesResolveToTheirPreviousFixedDates()
    {
        // FR-009: each converted branch carries window-relative forms only, and
        // every form resolves to exactly the fixed date the branch carried
        // before conversion, for the window year the branch targets.
        var parsed = LoadSeed();

        foreach (var (branchId, expectedDate, windowYear) in ConvertedBranches)
        {
            var branch = parsed.Outcomes
                .SelectMany(o => o.Rules)
                .Single(b => b.Id == branchId);

            var specs = RelativeDateLiterals(branch.When).ToList();
            Assert.True(specs.Count > 0, $"Branch {branchId} carries no window-relative date.");

            foreach (var spec in specs)
            {
                var ok = RelativeDateResolver.TryResolve(spec, new DateOnly(windowYear, 9, 1), out var date);
                Assert.True(ok, $"Branch {branchId}: {spec} did not resolve for window year {windowYear}.");
                Assert.Equal(expectedDate, date);
            }
        }
    }

    /// <summary>
    /// Fixed ISO date literals (deserialized as <see cref="FieldValue.Str"/>) on
    /// Date-typed fields, walked over the whole predicate tree.
    /// </summary>
    private static IEnumerable<(string Branch, string Field, string Value)> FixedDateLiterals(Predicate p)
    {
        switch (p)
        {
            case Predicate.FieldEq eq when IsDateField(eq.Field) && eq.Value is FieldValue.Str s:
                yield return ("eq", eq.Field, s.Value);
                break;
            case Predicate.FieldNeq neq when IsDateField(neq.Field) && neq.Value is FieldValue.Str s2:
                yield return ("neq", neq.Field, s2.Value);
                break;
            case Predicate.FieldCompare cmp when IsDateField(cmp.Field) && cmp.Value is FieldValue.Str s3:
                yield return (SymbolOf(cmp.Op), cmp.Field, s3.Value);
                break;
            case Predicate.FieldIn inP when IsDateField(inP.Field):
                foreach (var v in inP.Values.OfType<FieldValue.Str>())
                    yield return ("in", inP.Field, v.Value);
                break;
            case Predicate.AllOf all:
                foreach (var i in all.Items) foreach (var x in FixedDateLiterals(i)) yield return x;
                break;
            case Predicate.AnyOf any:
                foreach (var i in any.Items) foreach (var x in FixedDateLiterals(i)) yield return x;
                break;
            case Predicate.Not not:
                foreach (var x in FixedDateLiterals(not.Inner)) yield return x;
                break;
        }
    }

    /// <summary>Window-relative date specs, walked over the whole predicate tree.</summary>
    private static IEnumerable<RelativeDateSpec> RelativeDateLiterals(Predicate p)
    {
        switch (p)
        {
            case Predicate.FieldEq eq:
                if (eq.Value is FieldValue.RelativeDate rd) yield return rd.Spec;
                break;
            case Predicate.FieldNeq neq:
                if (neq.Value is FieldValue.RelativeDate rd2) yield return rd2.Spec;
                break;
            case Predicate.FieldCompare cmp:
                if (cmp.Value is FieldValue.RelativeDate rd3) yield return rd3.Spec;
                break;
            case Predicate.FieldIn inP:
                foreach (var v in inP.Values.OfType<FieldValue.RelativeDate>()) yield return v.Spec;
                break;
            case Predicate.AllOf all:
                foreach (var i in all.Items) foreach (var x in RelativeDateLiterals(i)) yield return x;
                break;
            case Predicate.AnyOf any:
                foreach (var i in any.Items) foreach (var x in RelativeDateLiterals(i)) yield return x;
                break;
            case Predicate.Not not:
                foreach (var x in RelativeDateLiterals(not.Inner)) yield return x;
                break;
        }
    }

    private static bool IsDateField(string field) =>
        FieldCatalogue.TryGetType(field, out var type) && type == FieldType.Date;

    private static string SymbolOf(CompareOp op) => op switch
    {
        CompareOp.Lt => "<",
        CompareOp.Lte => "<=",
        CompareOp.Gt => ">",
        CompareOp.Gte => ">=",
        _ => "?",
    };

    private static RuleSet LoadSeed()
    {
        var json = File.ReadAllText(Path.Combine(SeedDir, "rules.json"));
        return JsonSerializer.Deserialize<RuleSet>(json, RulesJson.Options)!;
    }

    // --- helpers ---

    private static string LocateSeedDirectory()
    {
        // Walk up from the test assembly to find the repo's src/...RulesEngineWorker/seed/ folder.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(
                dir.FullName,
                "src", "DfE.CheckPerformanceData.RulesEngineWorker", "seed");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(
            "Could not locate src/DfE.CheckPerformanceData.RulesEngineWorker/seed/ from " +
            AppContext.BaseDirectory);
    }
}
