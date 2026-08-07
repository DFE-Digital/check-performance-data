using System.Text.Json;
using System.Text.Json.Serialization;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.Journey.DateRules;
using DfE.CheckPerformanceData.Application.Journey.Validators;
using DfE.CheckPerformanceData.Domain.Enums;

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

    /// <summary>
    /// The same guard for the one rule that runs from code rather than config: the cross-field
    /// date rules in <see cref="PageDateRules"/> address questions by id, and nothing at runtime
    /// notices if the page or a question id is renamed in the flow JSON — the rules would simply
    /// stop matching and the validation would silently stop happening.
    /// </summary>
    [Fact]
    public void PageDateRules_QuestionIds_MatchTheShippedFlowConfig()
    {
        var page = AllFlowPages()
            .SingleOrDefault(p => p.Page.Id == PageDateRules.EalDetailsPageId);

        Assert.True(page.Page is not null,
            $"No flow config contains a page '{PageDateRules.EalDetailsPageId}', which " +
            $"PageDateRules addresses by id — its date rules would never run.");

        string[] ruleIds =
        [
            PageDateRules.StartedAtSchool,
            PageDateRules.FirstEnglishSchool,
            PageDateRules.ArrivedInEngland
        ];

        foreach (var id in ruleIds)
        {
            var question = page.Page!.Questions.SingleOrDefault(q => q.Id == id);
            Assert.True(question is not null,
                $"{page.File}: page '{page.Page.Id}' has no question '{id}', which PageDateRules " +
                $"compares — that rule would silently never fire.");
            Assert.True(question!.Type == QuestionType.Date,
                $"{page.File}: question '{id}' is {question.Type}, not Date — PageDateRules reads " +
                $"it as a date answer and would always see it as unanswered.");
        }

        // The optionality of the arrival date is load-bearing: three of the seven rules are only
        // evaluated when it has been filled in.
        Assert.True(
            page.Page!.Questions.Single(q => q.Id == PageDateRules.ArrivedInEngland).Optional,
            $"{page.File}: '{PageDateRules.ArrivedInEngland}' is no longer optional. PageDateRules " +
            $"treats a blank answer as 'rule not applicable' rather than an error — if the question " +
            $"is now mandatory, confirm that is still the intended behaviour.");
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

    private static IEnumerable<(string File, Question Question)> AllFlowQuestions() =>
        AllFlowPages().SelectMany(p => p.Page.Questions.Select(q => (p.File, q)));

    private static IEnumerable<(string File, JourneyPage Page)> AllFlowPages()
    {
        foreach (var file in Directory.GetFiles(LocateFlowsDirectory(), "*.json").Order())
        {
            var config = JsonSerializer.Deserialize<QuestionFlowConfig>(File.ReadAllText(file), JsonOptions)!;
            foreach (var page in config.Pages)
                yield return (Path.GetFileName(file), page);
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
