using System.Text.Json;
using System.Text.Json.Serialization;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.Journey.Validators;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

/// <summary>
/// Pins every name a shipped question-flow config references by string — <c>validator</c>
/// to an <see cref="IFormatValidator"/>, and <c>optionalWhen</c>/<c>visibleWhen</c> to an
/// <see cref="IJourneyCondition"/>. Nothing throws on an unresolved name at runtime: a
/// typo'd validator fails open (the format check is skipped, letting malformed answers
/// through) and a typo'd condition fails closed (the question stays mandatory, the option
/// stays hidden). Either way the config silently stops doing what it says.
/// </summary>
public sealed class QuestionFlowValidatorAlignmentTests
{
    // Mirrors QuestionFlowBlobClient's deserialization options.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public void EveryReferencedValidatorName_HasAnImplementation()
    {
        var implementedNames = ImplementedValidatorNames();

        var referenced = AllFlowQuestions()
            .Where(q => !string.IsNullOrWhiteSpace(q.Question.Validator))
            .Select(q => (q.File, Name: q.Question.Validator!, q.Question.Id))
            .Distinct();

        foreach (var (file, name, questionId) in referenced)
        {
            Assert.True(implementedNames.Contains(name),
                $"{file}: question '{questionId}' references validator '{name}', but no " +
                $"IFormatValidator implements it — the format check would silently be skipped.");
        }
    }

    /// <summary>
    /// The same guard for <c>optionalWhen</c> / <c>visibleWhen</c> condition names. These fail
    /// closed rather than open — an unresolved name leaves a question mandatory and an option
    /// hidden — so a typo silently disables the behaviour the config was asking for instead of
    /// throwing anywhere. Nothing else would catch it.
    /// </summary>
    [Fact]
    public void EveryReferencedConditionName_HasAnImplementation()
    {
        var implementedNames = ImplementedConditionNames();

        var referenced = AllFlowQuestions()
            .SelectMany(q => ReferencedConditionNames(q.Question)
                .Select(name => (q.File, Name: name, q.Question.Id)))
            .Distinct();

        foreach (var (file, name, questionId) in referenced)
        {
            Assert.True(implementedNames.Contains(name),
                $"{file}: question '{questionId}' references journey condition '{name}', but no " +
                $"IJourneyCondition implements it — the condition silently fails closed.");
        }
    }

    private static IEnumerable<string> ReferencedConditionNames(Question question)
    {
        foreach (var name in question.OptionalWhen ?? [])
            yield return name;

        foreach (var option in question.Options ?? [])
        foreach (var name in option.VisibleWhen ?? [])
            yield return name;
    }

    private static HashSet<string> ImplementedConditionNames()
    {
        var conditionTypes = typeof(IJourneyCondition).Assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false }
                && typeof(IJourneyCondition).IsAssignableFrom(t));

        return conditionTypes
            .Select(t => ((IJourneyCondition)Activator.CreateInstance(t)!).Name)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static HashSet<string> ImplementedValidatorNames()
    {
        var validatorTypes = typeof(IFormatValidator).Assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false }
                && typeof(IFormatValidator).IsAssignableFrom(t));

        return validatorTypes
            .Select(t => ((IFormatValidator)Activator.CreateInstance(t)!).Name)
            .ToHashSet(StringComparer.Ordinal);
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

    private static string LocateFlowsDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(
                dir.FullName,
                "src", "DfE.CheckPerformanceData.Web", "Data", "QuestionFlows");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(
            "Could not locate src/DfE.CheckPerformanceData.Web/Data/QuestionFlows from " +
            AppContext.BaseDirectory);
    }
}
