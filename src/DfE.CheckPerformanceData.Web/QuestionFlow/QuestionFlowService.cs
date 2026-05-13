using System.Text.Json;
using System.Text.Json.Serialization;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Web.QuestionFlow;

public sealed class QuestionFlowService(IWebHostEnvironment env) : IQuestionFlowService
{
    private readonly Dictionary<string, QuestionFlowConfig?> _cache = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public QuestionFlowConfig? GetConfig(WhatToChange whatToChange, CheckingWindowType checkingWindowType)
    {
        var key = $"{whatToChange}_{checkingWindowType}";
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        var path = Path.Combine(env.ContentRootPath, "Data", "QuestionFlows", $"{key}.json");
        if (!File.Exists(path))
        {
            _cache[key] = null;
            return null;
        }

        var config = JsonSerializer.Deserialize<QuestionFlowConfig>(File.ReadAllText(path), JsonOptions);
        _cache[key] = config;
        return config;
    }

    public JourneyPage GetPage(QuestionFlowConfig config, string pageId) =>
        config.Pages.First(p => p.Id == pageId);

    public string? GetNextPageId(QuestionFlowConfig config, string pageId, Dictionary<string, QuestionAnswer> answers)
    {
        var page = GetPage(config, pageId);

        foreach (var question in page.Questions)
        {
            if (question.Type != QuestionType.Radio || question.Options is null) continue;
            if (!answers.TryGetValue(question.Id, out var answer) || answer.TextValue is null) continue;

            var next = question.Options.FirstOrDefault(o => o.Value == answer.TextValue)?.NextPageId;
            if (next is not null) return next;
        }

        return page.NextPageId;
    }

    public List<string> BuildCurrentPath(QuestionFlowConfig config, Dictionary<string, QuestionAnswer> answers)
    {
        var path = new List<string>();
        var currentId = config.FirstPageId;

        while (currentId is not null)
        {
            var page = GetPage(config, currentId);

            // Stop if any radio on this page has no answer — we can't determine the branch
            if (page.Questions.Any(q => q.Type == QuestionType.Radio && !answers.ContainsKey(q.Id)))
                break;

            path.Add(currentId);
            currentId = GetNextPageId(config, currentId, answers);
        }

        return path;
    }
}
