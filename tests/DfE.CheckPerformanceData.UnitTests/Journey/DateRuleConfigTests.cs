using System.Text.Json;
using System.Text.Json.Serialization;
using DfE.CheckPerformanceData.Application;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.Journey.DateRules;
using Microsoft.Extensions.DependencyInjection;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

/// <summary>
/// Checks the <c>dateRules</c> in every shipped flow file. A bad rule does not throw at run time:
/// it skips its check, and the date is not validated. These tests do not pin one journey's rules
/// or wording. Check those by hand in the browser.
/// </summary>
public sealed class DateRuleConfigTests
{
    // The same options as FileSystemQuestionFlowClient.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public void DateRules_AreOnlyOnDateQuestions()
    {
        var offenders = AllFlowQuestions()
            .Where(q => q.Question.DateRules is { Count: > 0 } && q.Question.Type != QuestionType.Date)
            .Select(q => $"{q.File}: {q.Question.Id} is {q.Question.Type}");

        Assert.Empty(offenders);
    }

    /// <summary>
    /// Every date in every journey must not be in the future. The rule is first, so a date that
    /// is wrong by itself gives that message and not a comparison with another date.
    /// </summary>
    [Fact]
    public void EveryDateQuestion_HasNotInFutureAsItsFirstRule()
    {
        var offenders = AllFlowQuestions()
            .Where(q => q.Question.Type == QuestionType.Date
                && q.Question.DateRules is not [{ Check: DateRuleCheck.NotInFuture }, ..])
            .Select(q => $"{q.File}: {q.Question.Id}");

        Assert.Empty(offenders);
    }

    [Fact]
    public void EveryRule_HasAMessage()
    {
        var offenders = AllRules()
            .Where(r => string.IsNullOrWhiteSpace(r.Rule.Message))
            .Select(r => $"{r.File}: {r.QuestionId}");

        Assert.Empty(offenders);
    }

    // The enum converter also reads a number, and the evaluator skips a check that it does not know.
    [Fact]
    public void EveryRule_NamesAKnownCheck()
    {
        var offenders = AllRules()
            .Where(r => !Enum.IsDefined(r.Rule.Check))
            .Select(r => $"{r.File}: {r.QuestionId} has check {r.Rule.Check}");

        Assert.Empty(offenders);
    }

    // A notInFuture rule has no target, so {date} in its message becomes an empty string.
    [Fact]
    public void NotInFutureMessages_DoNotUseTheDatePlaceholder()
    {
        var offenders = AllRules()
            .Where(r => r.Rule.Check == DateRuleCheck.NotInFuture && r.Rule.Message.Contains("{date}", StringComparison.Ordinal))
            .Select(r => $"{r.File}: {r.QuestionId}");

        Assert.Empty(offenders);
    }

    [Fact]
    public void NotInFuture_NamesNoTarget_AndEveryComparison_NamesExactlyOneTarget()
    {
        foreach (var (file, questionId, rule) in AllRules())
        {
            var targets = new object?[] { rule.Question, rule.Fact, rule.Date }.Count(t => t is not null);
            var expected = rule.Check == DateRuleCheck.NotInFuture ? 0 : 1;
            Assert.True(targets == expected,
                $"{file}: a {rule.Check} rule on '{questionId}' names {targets} targets, not {expected}.");
        }
    }

    [Fact]
    public void EveryQuestionTarget_IsADateQuestionInTheSameFlow()
    {
        foreach (var (file, questionId, rule) in AllRules().Where(r => r.Rule.Question is not null))
        {
            var target = AllFlowQuestions().FirstOrDefault(q => q.File == file && q.Question.Id == rule.Question);
            Assert.True(target.Question is { Type: QuestionType.Date },
                $"{file}: a rule on '{questionId}' compares with '{rule.Question}', which is not a Date question in this flow.");
        }
    }

    /// <summary>
    /// A comparison between two questions is declared on both. The evaluator checks only the
    /// questions on the current page, so without the mirror rule the error does not show when
    /// the user changes the other date.
    /// </summary>
    [Fact]
    public void EveryQuestionComparison_HasTheMirrorRuleOnTheOtherQuestion()
    {
        var rules = AllRules().ToList();

        foreach (var (file, questionId, rule) in rules.Where(r => r.Rule.Question is not null))
        {
            var mirror = rule.Check == DateRuleCheck.OnOrBefore ? DateRuleCheck.OnOrAfter : DateRuleCheck.OnOrBefore;
            Assert.True(
                rules.Any(r => r.File == file && r.QuestionId == rule.Question
                    && r.Rule.Check == mirror && r.Rule.Question == questionId),
                $"{file}: '{questionId}' is {rule.Check} '{rule.Question}', but '{rule.Question}' has no " +
                $"{mirror} '{questionId}' rule.");
        }
    }

    [Fact]
    public void EveryReferencedDateFact_HasAnImplementation()
    {
        var implemented = ImplementedDateFactTypes()
            .Select(t => ((IJourneyDateFact)Activator.CreateInstance(t)!).Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var (file, questionId, rule) in AllRules().Where(r => r.Rule.Fact is not null))
        {
            Assert.True(implemented.Contains(rule.Fact!),
                $"{file}: a rule on '{questionId}' names date fact '{rule.Fact}', but no IJourneyDateFact has that name.");
        }
    }

    [Fact]
    public void EveryImplementedDateFact_IsRegisteredInTheContainer()
    {
        var services = new ServiceCollection();
        services.AddApplicationDependencies();

        var registered = services
            .Where(d => d.ServiceType == typeof(IJourneyDateFact))
            .Select(d => d.ImplementationType)
            .ToHashSet();

        foreach (var type in ImplementedDateFactTypes())
            Assert.True(registered.Contains(type), $"{type.Name} is not registered as an IJourneyDateFact.");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    // A date fact must have a parameterless constructor so that this test can read its name.
    // Change this helper if a fact needs a service.
    private static IEnumerable<Type> ImplementedDateFactTypes() =>
        typeof(IJourneyDateFact).Assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false } && typeof(IJourneyDateFact).IsAssignableFrom(t));

    private static IEnumerable<(string File, string QuestionId, DateRule Rule)> AllRules() =>
        AllFlowQuestions().SelectMany(q => (q.Question.DateRules ?? []).Select(r => (q.File, q.Question.Id, r)));

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
