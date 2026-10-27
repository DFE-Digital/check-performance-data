using System.Text.Json;
using System.Text.Json.Serialization;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Application.RulesEngine.Json;

namespace DfE.CheckPerformanceData.Application.UnitTests.RulesEngine;

/// <summary>
/// A flow file turns a journey on for its window type (docs/ks2-journeys.md, decision 1), but the
/// seed rules are keyed by window type too. When an outcome auto-decides on some window types,
/// it must have a rule that can auto-decide on the window type of every flow that sends it.
/// Otherwise a new flow file sends every request for that reason to Scrutiny, and nothing fails.
///
/// An outcome with no auto-decision on any window type (only the <c>otherwise</c> Scrutiny rule,
/// e.g. MergePupils, AddPupil) is Scrutiny by design and is not checked.
///
/// <see cref="QuestionFlowOutcomeKeyAlignmentTests"/> checks that each request routes to an outcome
/// key. This test checks that the outcome has a rule for the window type.
/// </summary>
public sealed class FlowReasonRuleCoverageTests
{
    // Mirrors FileSystemQuestionFlowClient's deserialization options.
    private static readonly JsonSerializerOptions FlowJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private const string WindowTypeField = "checkingWindowType";

    public static TheoryData<string> FlowFiles()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.GetFiles(LocateFlowsDirectory(), "*.json").Order())
            data.Add(Path.GetFileName(file));
        return data;
    }

    [Theory]
    [MemberData(nameof(FlowFiles))]
    public void Every_reason_that_auto_decides_somewhere_auto_decides_on_the_flows_window_type(string file)
    {
        var parts = Path.GetFileNameWithoutExtension(file).Split('_');
        var (prefix, windowType) = (parts[0], parts[1]);
        var outcomes = LoadRules().Outcomes.ToDictionary(o => o.Key, StringComparer.Ordinal);

        var missing = new List<string>();
        foreach (var contract in ContractStrings(prefix, LoadFlow(file)))
        {
            // Not routed (results enquiries), unmapped or unseeded: QuestionFlowOutcomeKeyAlignmentTests
            // and SeedRulesValidationTests own those.
            if (!AnswerFieldMap.WhatToChangeToOutcomeKey.TryGetValue(contract, out var outcomeKey)) continue;
            if (!outcomes.TryGetValue(outcomeKey, out var outcome)) continue;

            var autoDecides = outcome.Rules.Where(r => r.Status != DecisionStatus.Scrutiny).ToList();
            if (autoDecides.Count > 0 && autoDecides.All(r => ExcludesWindow(r.When, windowType)))
                missing.Add($"'{contract}' → {outcomeKey} ({string.Join(", ", autoDecides.Select(r => r.Id))})");
        }

        Assert.True(missing.Count == 0,
            $"{file}: these reasons auto-decide on other window types but have no rule that can " +
            $"auto-decide on {windowType}, so every request goes to Scrutiny:\n  " +
            string.Join("\n  ", missing));
    }

    /// <summary>
    /// True when <paramref name="predicate"/> can never match on <paramref name="windowType"/>
    /// because it demands another window type. Anything this cannot decide (a <c>not</c>, any other
    /// field) counts as "can match", so the test fails only on a sure gap.
    /// </summary>
    private static bool ExcludesWindow(Predicate predicate, string windowType) => predicate switch
    {
        Predicate.FieldEq { Field: WindowTypeField, Value: FieldValue.Str s } => s.Value != windowType,
        Predicate.FieldNeq { Field: WindowTypeField, Value: FieldValue.Str s } => s.Value == windowType,
        Predicate.FieldIn { Field: WindowTypeField } f =>
            !f.Values.OfType<FieldValue.Str>().Any(v => v.Value == windowType),
        Predicate.AllOf all => all.Items.Any(i => ExcludesWindow(i, windowType)),
        Predicate.AnyOf any => any.Items.All(i => ExcludesWindow(i, windowType)),
        _ => false
    };

    // The same contract strings QuestionFlowOutcomeKeyAlignmentTests derives: the bare prefix, or
    // "{prefix} - {option value}" for each option of a useAsRequestType question.
    private static IEnumerable<string> ContractStrings(string prefix, QuestionFlowConfig config)
    {
        var flagged = config.Pages.SelectMany(p => p.Questions).Where(q => q.UseAsRequestType).ToList();
        if (flagged.Count == 0) return [prefix];
        return flagged.SelectMany(q => q.Options ?? []).Select(o => $"{prefix} - {o.Value}");
    }

    private static QuestionFlowConfig LoadFlow(string file) =>
        JsonSerializer.Deserialize<QuestionFlowConfig>(
            File.ReadAllText(Path.Combine(LocateFlowsDirectory(), file)), FlowJsonOptions)!;

    private static RuleSet LoadRules() =>
        JsonSerializer.Deserialize<RuleSet>(
            File.ReadAllText(Path.Combine(LocateSeedDirectory(), "rules.json")), RulesJson.Options)!;

    private static string LocateFlowsDirectory() => Locate("src", "DfE.CheckPerformanceData.Web", "Data", "QuestionFlows");

    private static string LocateSeedDirectory() => Locate("src", "DfE.CheckPerformanceData.RulesEngineWorker", "seed");

    private static string Locate(params string[] parts)
    {
        // Walk up from the test assembly to find the repo folder.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine([dir.FullName, .. parts]);
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(
            $"Could not locate {string.Join('/', parts)} from {AppContext.BaseDirectory}");
    }
}
