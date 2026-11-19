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

    [Fact]
    public void SeedRulesJson_EveryRelativeDateLiteral_ResolvesForARepresentativeWindowStart()
    {
        // Generic validity: every window-relative form the seed carries must resolve
        // to a concrete date for a representative window start. A leap-year window
        // start is used so a dayMonthOfWindowYear that needs 29 February also
        // resolves. No branch id or resolved date is pinned here (AGENTS.md: test
        // that the config is valid, not what one set of rules contains).
        var parsed = LoadSeed();
        var windowStart = new DateOnly(2028, 9, 1);

        var specs = parsed.Outcomes
            .SelectMany(o => o.Rules)
            .SelectMany(b => RelativeDateLiterals(b.When))
            .ToList();

        Assert.NotEmpty(specs);
        foreach (var spec in specs)
        {
            var ok = RelativeDateResolver.TryResolve(spec, windowStart, out _);
            Assert.True(ok, $"Seed rules.json: {spec} did not resolve for window start {windowStart}.");
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
