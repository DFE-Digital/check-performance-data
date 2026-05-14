using System.Text.Json;
using System.Text.Json.Serialization;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Domain.Enums;
using Microsoft.Extensions.Caching.Memory;

namespace DfE.CheckPerformanceData.Web.QuestionFlow;

public sealed class QuestionFlowService(IWebHostEnvironment env, IMemoryCache cache) : IQuestionFlowService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public QuestionFlowConfig? GetConfig(WhatToChange whatToChange, CheckingWindowType checkingWindowType)
    {
        var key = $"qflow_{whatToChange}_{checkingWindowType}";
        return cache.GetOrCreate(key, entry =>
        {
            entry.Priority = CacheItemPriority.NeverRemove;

            var path = Path.Combine(env.ContentRootPath, "Data", "QuestionFlows",
                $"{whatToChange}_{checkingWindowType}.json");

            return File.Exists(path)
                ? JsonSerializer.Deserialize<QuestionFlowConfig>(File.ReadAllText(path), JsonOptions)
                : null;
        });
    }

    public JourneyPage? GetPage(QuestionFlowConfig config, string pageId) =>
        config.Pages.FirstOrDefault(p => p.Id == pageId);

    public string? GetNextPageId(QuestionFlowConfig config, string pageId, Dictionary<string, QuestionAnswer> answers)
    {
        var page = GetPage(config, pageId);
        if (page is null) return null;

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
            if (page is null) break;

            // Stop if any radio on this page has no answer — we can't determine the branch
            if (page.Questions.Any(q => q.Type == QuestionType.Radio && !answers.ContainsKey(q.Id)))
                break;

            path.Add(currentId);
            currentId = GetNextPageId(config, currentId, answers);
        }

        return path;
    }
}
