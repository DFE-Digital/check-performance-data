using System.Text.Json;
using System.Text.Json.Serialization;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.RulesEngine;

namespace DfE.CheckPerformanceData.Application.UnitTests.RulesEngine;

/// <summary>
/// Pins the Web question-flow configs to <see cref="AnswerFieldMap.WhatToChangeToOutcomeKey"/>:
/// every <c>WhatToChange</c> contract string a flow can produce — the bare flow prefix,
/// or <c>"{prefix} - {reason option value}"</c> for each option of a
/// <c>useAsRequestType</c> question — must route to an outcome key. A reason option
/// added (or a value renamed) without a matching map entry would otherwise silently
/// send every such request to <c>_unknown</c>/Scrutiny.
/// </summary>
public sealed class QuestionFlowOutcomeKeyAlignmentTests
{
    // Mirrors QuestionFlowBlobClient's deserialization options.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static TheoryData<string> FlowFiles()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.GetFiles(LocateFlowsDirectory(), "*.json").Order())
            data.Add(Path.GetFileName(file));
        return data;
    }

    [Fact]
    public void FlowConfigsExist()
    {
        Assert.NotEmpty(Directory.GetFiles(LocateFlowsDirectory(), "*.json"));
    }

    /// <summary>
    /// Flow prefixes whose requests never reach the rules engine, so they have no outcome key and
    /// must not be given a fake one — an entry here would make
    /// <c>OutcomeDeletionPolicy.IsFormBound</c> protect a nonexistent outcome and would tell
    /// <c>SeedRulesValidationTests</c> to expect rules that were deliberately not written.
    ///
    /// <c>IncorrectGrade</c> (AB#296648) is a 16-19 results enquiry: it is persisted as a
    /// <c>RequestType.ResultsEnquiry</c> row plus a document blob, and is NOT enqueued. Whether it
    /// ever gets rules-engine or Zendesk processing is a PARKED decision. When that is answered and
    /// enqueueing is added, remove the prefix from here and add its real outcome key(s) to
    /// <see cref="AnswerFieldMap.WhatToChangeToOutcomeKey"/> — this list going empty is the signal
    /// that every flow routes.
    /// </summary>
    private static readonly string[] FlowPrefixesThatDoNotRouteToTheRulesEngine = ["IncorrectGrade"];

    [Theory]
    [MemberData(nameof(FlowFiles))]
    public void EveryContractString_RoutesToAnOutcomeKey(string fileName)
    {
        var path = Path.Combine(LocateFlowsDirectory(), fileName);
        var config = JsonSerializer.Deserialize<QuestionFlowConfig>(File.ReadAllText(path), JsonOptions);
        Assert.NotNull(config);

        var prefix = fileName.Split('_')[0];

        if (FlowPrefixesThatDoNotRouteToTheRulesEngine.Contains(prefix, StringComparer.Ordinal))
        {
            // Assert the *absence* too, so adding a speculative map entry fails here rather than
            // quietly binding an enquiry journey to rules-engine routing nobody signed off.
            AssertNotRoutable(prefix);
            return;
        }

        var flagged = config.Pages
            .SelectMany(p => p.Questions)
            .Where(q => q.UseAsRequestType)
            .ToList();

        if (flagged.Count == 0)
        {
            AssertRoutable(prefix);
            return;
        }

        foreach (var question in flagged)
        {
            Assert.True(question.Options is { Count: > 0 },
                $"{fileName}: useAsRequestType question '{question.Id}' must be a Radio with options.");

            foreach (var option in question.Options!)
                AssertRoutable($"{prefix} - {option.Value}");
        }
    }

    private static void AssertRoutable(string contract) =>
        Assert.True(AnswerFieldMap.WhatToChangeToOutcomeKey.ContainsKey(contract),
            $"WhatToChange '{contract}' has no entry in AnswerFieldMap.WhatToChangeToOutcomeKey — " +
            "every request for it would resolve to _unknown and fall to Scrutiny.");

    private static void AssertNotRoutable(string contract) =>
        Assert.False(AnswerFieldMap.WhatToChangeToOutcomeKey.ContainsKey(contract),
            $"WhatToChange '{contract}' is listed as not routing to the rules engine, but it now has " +
            "an entry in AnswerFieldMap.WhatToChangeToOutcomeKey. If downstream processing has been " +
            "decided, remove it from FlowPrefixesThatDoNotRouteToTheRulesEngine.");

    // ── Question id / option value alignment ────────────────────────────────

    /// <summary>
    /// Question ids the AnswerFieldMap consumes whose flows are not authored yet
    /// (KS2 / Post16). Remove an id when its flow config exists so the test below
    /// guards it.
    /// </summary>
    private static readonly string[] PendingFlowQuestionIds =
    [
        "date-added-to-roll",
        "removal-reason-at-school",
        "continuing-ks2-studies",
        "pupil-age",
    ];

    [Fact]
    public void EveryMappedQuestionId_ExistsInAnAuthoredFlow()
    {
        var flowQuestionIds = AllFlowQuestions().Select(q => q.Question.Id).ToHashSet(StringComparer.Ordinal);

        var consumedIds = AnswerFieldMap.QuestionToField.Keys
            .Concat(AnswerFieldMap.RadioFanOut.Keys)
            .Concat(AnswerFieldMap.TranslatedQuestions.Keys)
            .Append(AnswerFieldMap.SatExamsQuestionId)
            .Except(PendingFlowQuestionIds, StringComparer.Ordinal);

        foreach (var id in consumedIds)
        {
            Assert.True(flowQuestionIds.Contains(id),
                $"AnswerFieldMap consumes question id '{id}' but no authored flow config asks it — " +
                "its canonical field would always be Unknown.");
        }
    }

    [Fact]
    public void EveryFanOutTriggerValue_ExistsOnItsFlowQuestion()
    {
        foreach (var (questionId, fanOut) in AnswerFieldMap.RadioFanOut)
        foreach (var (field, trigger) in fanOut)
        {
            AssertOptionValueExists(questionId, trigger,
                $"RadioFanOut field '{field}' triggers on '{trigger}'");
        }
    }

    [Fact]
    public void EveryTranslatedValue_ExistsOnItsFlowQuestion()
    {
        foreach (var (questionId, (field, values)) in AnswerFieldMap.TranslatedQuestions)
        foreach (var raw in values.Keys)
        {
            AssertOptionValueExists(questionId, raw,
                $"TranslatedQuestions field '{field}' maps raw value '{raw}'");
        }
    }

    private static void AssertOptionValueExists(string questionId, string optionValue, string mapDescription)
    {
        var questions = AllFlowQuestions()
            .Where(q => q.Question.Id == questionId)
            .ToList();

        Assert.True(questions.Count > 0,
            $"{mapDescription}, but no authored flow asks question '{questionId}'.");
        Assert.True(questions.Any(q => q.Question.Options?.Any(o => o.Value == optionValue) == true),
            $"{mapDescription}, but question '{questionId}' has no option with value '{optionValue}' " +
            $"in any authored flow — that mapping can never fire.");
    }

    private static IEnumerable<(string File, Question Question)> AllFlowQuestions()
    {
        var dir = LocateFlowsDirectory();
        foreach (var file in Directory.GetFiles(dir, "*.json").Order())
        {
            var config = JsonSerializer.Deserialize<QuestionFlowConfig>(File.ReadAllText(file), JsonOptions)!;
            foreach (var page in config.Pages)
            foreach (var question in page.Questions)
                yield return (Path.GetFileName(file), question);
        }
    }

    private static string LocateFlowsDirectory()
    {
        // Walk up from the test assembly to find the repo's Web question-flow configs.
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
