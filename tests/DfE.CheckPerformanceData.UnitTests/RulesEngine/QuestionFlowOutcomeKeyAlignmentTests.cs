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

    [Theory]
    [MemberData(nameof(FlowFiles))]
    public void EveryContractString_RoutesToAnOutcomeKey(string fileName)
    {
        var path = Path.Combine(LocateFlowsDirectory(), fileName);
        var config = JsonSerializer.Deserialize<QuestionFlowConfig>(File.ReadAllText(path), JsonOptions);
        Assert.NotNull(config);

        var prefix = fileName.Split('_')[0];

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
