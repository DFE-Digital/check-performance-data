using System.Text.Json;
using System.Text.Json.Serialization;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.Journey.DateRules;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

public sealed class DateRuleEvaluatorTests
{
    // The same options as FileSystemQuestionFlowClient.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public void DateRules_ReadFromTheFlowJson()
    {
        const string json = """
            {
              "id": "q", "type": "Date", "title": "When?",
              "dateRules": [
                { "check": "notInFuture", "message": "past" },
                { "check": "onOrBefore", "question": "other", "message": "before" },
                { "check": "onOrAfter", "fact": "cohort.start", "message": "cohort" },
                { "check": "onOrAfter", "date": "2023-09-01", "message": "fixed" }
              ]
            }
            """;

        var question = JsonSerializer.Deserialize<Question>(json, JsonOptions)!;

        Assert.Collection(question.DateRules!,
            r => Assert.Equal(new DateRule { Check = DateRuleCheck.NotInFuture, Message = "past" }, r),
            r => Assert.Equal(new DateRule { Check = DateRuleCheck.OnOrBefore, Question = "other", Message = "before" }, r),
            r => Assert.Equal(new DateRule { Check = DateRuleCheck.OnOrAfter, Fact = "cohort.start", Message = "cohort" }, r),
            r => Assert.Equal(new DateRule { Check = DateRuleCheck.OnOrAfter, Date = new DateOnly(2023, 9, 1), Message = "fixed" }, r));
    }

    private static readonly DateOnly Today = new(2026, 6, 15);
    private const string Pupil = "Alice Smith";

    private static readonly JourneyConditionContext Context = new()
    {
        Journey = new RequestState(),
        User = new JourneyUserContext()
    };

    // ── notInFuture ─────────────────────────────────────────────────────────

    [Fact]
    public void NotInFuture_FutureDate_Fails()
    {
        var page = Page(DateQuestion("a", NotInFuture("{pupilName} must be in the past")));

        var violation = Assert.Single(Evaluate(page, Answers(("a", new DateOnly(2026, 6, 16)))));

        Assert.Equal(new DateFieldViolation("a", "Alice Smith must be in the past"), violation);
    }

    [Fact]
    public void NotInFuture_Today_Passes()
    {
        var page = Page(DateQuestion("a", NotInFuture("past")));

        Assert.Empty(Evaluate(page, Answers(("a", Today))));
    }

    // ── Comparisons with another question ───────────────────────────────────

    [Fact]
    public void OnOrAfterQuestion_EarlierDate_FailsWithTheTargetDateInTheMessage()
    {
        var page = Page(
            DateQuestion("a", OnOrAfter(question: "b", message: "a must be on or after {date}")),
            DateQuestion("b"));

        var violation = Assert.Single(Evaluate(page,
            Answers(("a", new DateOnly(2023, 9, 4)), ("b", new DateOnly(2024, 1, 8)))));

        Assert.Equal(new DateFieldViolation("a", "a must be on or after 8 January 2024"), violation);
    }

    [Fact]
    public void OnOrBeforeQuestion_LaterDate_Fails()
    {
        var page = Page(
            DateQuestion("a", OnOrBefore(question: "b", message: "a must be on or before {date}")),
            DateQuestion("b"));

        var violation = Assert.Single(Evaluate(page,
            Answers(("a", new DateOnly(2024, 1, 9)), ("b", new DateOnly(2024, 1, 8)))));

        Assert.Equal("a must be on or before 8 January 2024", violation.Message);
    }

    [Fact]
    public void Comparison_EqualDates_Pass()
    {
        var page = Page(
            DateQuestion("a", OnOrAfter(question: "b", message: "after")),
            DateQuestion("b", OnOrBefore(question: "a", message: "before")));

        Assert.Empty(Evaluate(page, Answers(("a", Today), ("b", Today))));
    }

    [Fact]
    public void Comparison_TargetOnAnotherPage_UsesTheJourneyAnswer()
    {
        // The target question is not on this page. Its answer comes from the journey.
        var page = Page(DateQuestion("admitted", OnOrAfter(question: "born", message: "after birth")));

        var violation = Assert.Single(Evaluate(page,
            Answers(("admitted", new DateOnly(2010, 1, 1)), ("born", new DateOnly(2011, 1, 1)))));

        Assert.Equal("admitted", violation.QuestionId);
    }

    [Fact]
    public void Comparison_TargetUnanswered_IsSkipped()
    {
        var page = Page(DateQuestion("a", OnOrAfter(question: "b", message: "after")), DateQuestion("b"));

        Assert.Empty(Evaluate(page, Answers(("a", new DateOnly(2020, 1, 1)))));
    }

    [Fact]
    public void Comparison_TargetInvalidDate_IsSkipped()
    {
        var page = Page(DateQuestion("a", OnOrAfter(question: "b", message: "after")), DateQuestion("b"));
        var answers = Answers(("a", new DateOnly(2020, 1, 1)));
        answers["b"] = new QuestionAnswer { DateValue = new DateAnswer { Day = 31, Month = 2, Year = 2026 } };

        Assert.Empty(Evaluate(page, answers));
    }

    // ── Fixed date ──────────────────────────────────────────────────────────

    [Fact]
    public void OnOrAfterFixedDate_EarlierDate_Fails()
    {
        var page = Page(DateQuestion("a", OnOrAfter(date: new DateOnly(2023, 9, 1), message: "too old")));

        Assert.Equal("too old", Assert.Single(Evaluate(page, Answers(("a", new DateOnly(2023, 8, 31))))).Message);
        Assert.Empty(Evaluate(page, Answers(("a", new DateOnly(2023, 9, 1)))));
    }

    // ── Date facts ──────────────────────────────────────────────────────────

    [Fact]
    public void OnOrAfterFact_EarlierDate_Fails()
    {
        var page = Page(DateQuestion("a", OnOrAfter(fact: "test.fact", message: "before fact")));
        var evaluator = Evaluator(Fact("test.fact", new DateOnly(2022, 9, 1)));

        var violation = Assert.Single(evaluator.Evaluate(page, Answers(("a", new DateOnly(2022, 8, 31))), Context, Today, Pupil));

        Assert.Equal("before fact", violation.Message);
    }

    [Fact]
    public void Fact_GetsTheEvaluatorsToday()
    {
        var fact = Fact("test.fact", new DateOnly(2000, 1, 1));
        var page = Page(DateQuestion("a", OnOrAfter(fact: "test.fact", message: "m")));

        Evaluator(fact).Evaluate(page, Answers(("a", new DateOnly(2001, 1, 1))), Context, Today, Pupil);

        fact.Received().Value(Context, Today);
    }

    [Fact]
    public void Fact_WithNoValue_SkipsTheRuleAndLogsAWarning()
    {
        var logger = Substitute.For<ILogger>();
        var page = Page(DateQuestion("a", OnOrAfter(fact: "test.fact", message: "m")));
        var evaluator = new DateRuleEvaluator([Fact("test.fact", null)], logger);

        Assert.Empty(evaluator.Evaluate(page, Answers(("a", new DateOnly(1900, 1, 1))), Context, Today, Pupil));
        Assert.Equal(1, WarningCount(logger));
    }

    [Fact]
    public void Fact_NotRegistered_SkipsTheRuleAndLogsAWarning()
    {
        var logger = Substitute.For<ILogger>();
        var page = Page(DateQuestion("a", OnOrAfter(fact: "unknown.fact", message: "m")));
        var evaluator = new DateRuleEvaluator([], logger);

        Assert.Empty(evaluator.Evaluate(page, Answers(("a", new DateOnly(1900, 1, 1))), Context, Today, Pupil));
        Assert.Equal(1, WarningCount(logger));
    }

    // ── Order and scope ─────────────────────────────────────────────────────

    [Fact]
    public void FirstFailingRule_GivesTheOnlyMessage()
    {
        // The date is in the future and also earlier than b. The first rule in the list wins.
        var page = Page(
            DateQuestion("a", NotInFuture("future"), OnOrAfter(question: "b", message: "order")),
            DateQuestion("b"));

        var violation = Assert.Single(Evaluate(page,
            Answers(("a", new DateOnly(2026, 7, 1)), ("b", new DateOnly(2026, 8, 1)))));

        Assert.Equal("future", violation.Message);
    }

    [Fact]
    public void BothSidesOfAComparison_OnOnePage_AreBothFlagged_InPageOrder()
    {
        var page = Page(
            DateQuestion("a", OnOrAfter(question: "b", message: "a side")),
            DateQuestion("b", OnOrBefore(question: "a", message: "b side")));

        var violations = Evaluate(page, Answers(("a", new DateOnly(2023, 1, 1)), ("b", new DateOnly(2024, 1, 1))));

        Assert.Equal(["a", "b"], violations.Select(v => v.QuestionId));
    }

    [Fact]
    public void UnansweredOrInvalidQuestion_IsSkipped()
    {
        var page = Page(DateQuestion("a", NotInFuture("future")), DateQuestion("b", NotInFuture("future")));
        var answers = new Dictionary<string, QuestionAnswer>
        {
            ["b"] = new() { DateValue = new DateAnswer { Day = 0, Month = 6, Year = 2099 } }
        };

        Assert.Empty(Evaluate(page, answers));
    }

    [Fact]
    public void QuestionWithoutRules_IsSkipped()
    {
        var page = Page(DateQuestion("a"));

        Assert.Empty(Evaluate(page, Answers(("a", new DateOnly(2099, 1, 1)))));
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static DateRuleEvaluator Evaluator(params IJourneyDateFact[] facts) =>
        new(facts, NullLogger.Instance);

    // LogWarning calls the generic Log<TState> with an internal state type, so Received().Log(...)
    // with Arg.Any<object>() does not match it. Count the calls instead.
    private static int WarningCount(ILogger logger) =>
        logger.ReceivedCalls().Count(c =>
            c.GetMethodInfo().Name == nameof(ILogger.Log)
            && c.GetArguments()[0] is LogLevel.Warning);

    private static IReadOnlyList<DateFieldViolation> Evaluate(
        JourneyPage page, IReadOnlyDictionary<string, QuestionAnswer> answers) =>
        Evaluator().Evaluate(page, answers, Context, Today, Pupil);

    private static IJourneyDateFact Fact(string name, DateOnly? value)
    {
        var fact = Substitute.For<IJourneyDateFact>();
        fact.Name.Returns(name);
        fact.Value(Arg.Any<JourneyConditionContext>(), Arg.Any<DateOnly>()).Returns(value);
        return fact;
    }

    private static JourneyPage Page(params Question[] questions) =>
        new() { Id = "page", Questions = [.. questions] };

    private static Question DateQuestion(string id, params DateRule[] rules) =>
        new() { Id = id, Type = QuestionType.Date, Title = id, DateRules = rules.Length == 0 ? null : rules };

    private static DateRule NotInFuture(string message) =>
        new() { Check = DateRuleCheck.NotInFuture, Message = message };

    private static DateRule OnOrAfter(string message, string? question = null, string? fact = null, DateOnly? date = null) =>
        new() { Check = DateRuleCheck.OnOrAfter, Question = question, Fact = fact, Date = date, Message = message };

    private static DateRule OnOrBefore(string message, string? question = null, string? fact = null, DateOnly? date = null) =>
        new() { Check = DateRuleCheck.OnOrBefore, Question = question, Fact = fact, Date = date, Message = message };

    private static Dictionary<string, QuestionAnswer> Answers(params (string Id, DateOnly Date)[] dates) =>
        dates.ToDictionary(
            d => d.Id,
            d => new QuestionAnswer { DateValue = new DateAnswer { Day = d.Date.Day, Month = d.Date.Month, Year = d.Date.Year } },
            StringComparer.Ordinal);
}
