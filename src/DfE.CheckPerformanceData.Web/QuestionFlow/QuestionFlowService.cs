using System.Text.Json;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Web.QuestionFlow;

public sealed class QuestionFlowService(IWebHostEnvironment env) : IQuestionFlowService
{
    private readonly Dictionary<string, QuestionFlowConfig?> _cache = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public QuestionFlowConfig? GetConfig(WhatToChange whatToChange, KeyStages keyStage)
    {
        var key = $"{whatToChange}_{keyStage}";
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        var path = Path.Combine(env.ContentRootPath, "Data", "QuestionFlows", $"{key}.json");
        if (!File.Exists(path))
        {
            _cache[key] = null;
            return null;
        }

        var json = File.ReadAllText(path);
        var config = JsonSerializer.Deserialize<QuestionFlowConfig>(json, JsonOptions);
        _cache[key] = config;
        return config;
    }

    public Question GetQuestion(QuestionFlowConfig config, string questionId) =>
        config.Questions.First(q => q.Id == questionId);

    public string? GetNextQuestionId(QuestionFlowConfig config, string questionId, QuestionAnswer? answer)
    {
        var question = GetQuestion(config, questionId);

        if (question.Type == QuestionType.Radio && answer?.TextValue is not null && question.Options is not null)
        {
            var option = question.Options.FirstOrDefault(o => o.Value == answer.TextValue);
            if (option?.NextQuestionId is not null)
                return option.NextQuestionId;
        }

        return question.NextQuestionId;
    }

    public List<string> BuildCurrentPath(QuestionFlowConfig config, Dictionary<string, QuestionAnswer> answers)
    {
        var path = new List<string>();
        var currentId = config.FirstQuestionId;

        while (currentId is not null)
        {
            var question = GetQuestion(config, currentId);

            // Without an answer we can't determine which radio branch to take
            if (question.Type == QuestionType.Radio && !answers.ContainsKey(currentId))
                break;

            path.Add(currentId);
            answers.TryGetValue(currentId, out var answer);
            currentId = GetNextQuestionId(config, currentId, answer);
        }

        return path;
    }
}
