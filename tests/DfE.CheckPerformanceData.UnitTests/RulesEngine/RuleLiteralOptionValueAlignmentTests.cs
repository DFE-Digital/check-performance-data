using System.Text.Json;
using System.Text.Json.Serialization;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Application.RulesEngine.Json;

namespace DfE.CheckPerformanceData.Application.UnitTests.RulesEngine;

/// <summary>
/// Pins the seed rules' string literals to the authored flow configs for every field
/// the mapper fills by plain copy (<see cref="AnswerFieldMap.QuestionToField"/>).
///
/// Such a field carries the answer's <c>RawValue</c> — the radio option's *value*, never
/// its display label — and <c>RulesEngine.Equal</c> compares ordinally, so a rule written
/// against the label can never match. It fails silently: the branch is skipped and the
/// request falls to the outcome's terminal <c>otherwise</c> (Scrutiny), which looks like
/// a policy decision rather than a bug. <c>NOR-POST16-REASON</c> was written that way and
/// auto-approved nothing.
///
/// <see cref="QuestionFlowOutcomeKeyAlignmentTests"/> already guards the two mapped
/// vocabularies (<c>RadioFanOut</c> triggers and <c>TranslatedQuestions</c> raw values);
/// this covers the third and last one.
/// </summary>
public sealed class RuleLiteralOptionValueAlignmentTests
{
    // Mirrors QuestionFlowBlobClient's deserialization options.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public void EveryStringLiteral_OnAPlainCopiedField_IsAnOptionValueOfItsSourceQuestion()
    {
        var rules = LoadSeedRules();

        // Canonical field → the flow questions that feed it, keeping only those a flow
        // actually authors with options. An Autocomplete (countryOfOrigin) carries a code
        // rather than a listed option, and an unauthored placeholder id has nothing to
        // check against, so both drop out here.
        var sourceQuestions = AnswerFieldMap.QuestionToField
            .GroupBy(kvp => kvp.Value, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => AllFlowQuestions()
                    .Where(q => g.Any(kvp => kvp.Key == q.Question.Id))
                    .Where(q => q.Question.Options is { Count: > 0 })
                    .ToList(),
                StringComparer.Ordinal);

        foreach (var outcome in rules.Outcomes)
        foreach (var branch in outcome.Rules)
        foreach (var (field, literal) in StringLiterals(branch.When))
        {
            if (!sourceQuestions.TryGetValue(field, out var questions) || questions.Count == 0) continue;

            var known = questions
                .SelectMany(q => q.Question.Options!)
                .Select(o => o.Value)
                .ToHashSet(StringComparer.Ordinal);

            Assert.True(known.Contains(literal),
                $"Rule '{branch.Id}' compares {field} against \"{literal}\", but the question(s) that " +
                $"fill it ({string.Join(", ", questions.Select(q => $"{q.File}:{q.Question.Id}"))}) offer " +
                $"only [{string.Join(", ", known.Order(StringComparer.Ordinal))}]. The mapper copies the " +
                "option value, not the label, so that branch can never match.");
        }
    }

    /// <summary>Every string literal a predicate compares a field against, flattened.</summary>
    private static IEnumerable<(string Field, string Literal)> StringLiterals(Predicate predicate)
    {
        switch (predicate)
        {
            case Predicate.AllOf all:
                foreach (var item in all.Items)
                foreach (var hit in StringLiterals(item)) yield return hit;
                break;

            case Predicate.AnyOf any:
                foreach (var item in any.Items)
                foreach (var hit in StringLiterals(item)) yield return hit;
                break;

            case Predicate.Not not:
                foreach (var hit in StringLiterals(not.Inner)) yield return hit;
                break;

            case Predicate.FieldEq { Value: FieldValue.Str s } eq:
                yield return (eq.Field, s.Value);
                break;

            case Predicate.FieldNeq { Value: FieldValue.Str s } neq:
                yield return (neq.Field, s.Value);
                break;

            case Predicate.FieldIn inP:
                foreach (var value in inP.Values.OfType<FieldValue.Str>())
                    yield return (inP.Field, value.Value);
                break;
        }
    }

    private static RuleSet LoadSeedRules()
    {
        var json = File.ReadAllText(Path.Combine(LocateSeedDirectory(), "rules.json"));
        var parsed = JsonSerializer.Deserialize<RuleSet>(json, RulesJson.Options)!;
        var validation = new RuleSetValidator().Validate(parsed);
        Assert.True(validation.IsValid, string.Join("\n  ", validation.Errors));
        return validation.ResolvedRules!;
    }

    private static IEnumerable<(string File, Question Question)> AllFlowQuestions()
    {
        foreach (var file in Directory.GetFiles(LocateFlowsDirectory(), "*.json").Order())
        {
            var config = JsonSerializer.Deserialize<QuestionFlowConfig>(File.ReadAllText(file), JsonOptions)!;
            foreach (var page in config.Pages)
            foreach (var question in page.Questions)
                yield return (Path.GetFileName(file), question);
        }
    }

    private static string LocateFlowsDirectory() =>
        LocateFromRepoRoot(Path.Combine("src", "DfE.CheckPerformanceData.Web", "Data", "QuestionFlows"));

    private static string LocateSeedDirectory() =>
        LocateFromRepoRoot(Path.Combine("src", "DfE.CheckPerformanceData.RulesEngineWorker", "seed"));

    private static string LocateFromRepoRoot(string relative)
    {
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
