using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Domain.Enums;
using Microsoft.Extensions.Caching.Memory;

namespace DfE.CheckPerformanceData.Application.Journey;

public sealed class QuestionFlowService(IQuestionFlowBlobClient blobClient, IMemoryCache cache) : IQuestionFlowService
{
    public async Task<QuestionFlowConfig?> GetConfigAsync(WhatToChange whatToChange, CheckingWindowType checkingWindowType)
    {
        var key = $"qflow_{whatToChange}_{checkingWindowType}";
        if (cache.TryGetValue<QuestionFlowConfig>(key, out var cached))
            return cached;

        var config = await blobClient.GetConfigAsync(whatToChange, checkingWindowType);
        cache.Set(key, config, new MemoryCacheEntryOptions { Priority = CacheItemPriority.NeverRemove });
        return config;
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

    public JourneyNavigation? GetNavigationGuard(QuestionFlowConfig config, RequestState journey, string pageId)
    {
        if (journey.QuestionHistory.Contains(pageId)) return null;

        var expectedNext = journey.QuestionHistory.Count == 0
            ? config.FirstPageId
            : GetNextPageId(config, journey.QuestionHistory.Last(), journey.QuestionAnswers);

        if (expectedNext is null) return new RedirectToJourneySummary();
        if (pageId == expectedNext) return null;
        return new RedirectToJourneyPage(expectedNext);
    }

    public string BuildContentKey(Guid windowId, JourneyPage page, Dictionary<string, QuestionAnswer> answers,
        RequestState journey, QuestionFlowConfig config)
    {
        var whatToChange = journey.SelectedWhatToChange?.ToString().ToLower() ?? "unknown";

        var pageIndex = journey.QuestionHistory.IndexOf(page.Id);
        IEnumerable<string> historyBeforePage = pageIndex >= 0
            ? journey.QuestionHistory.Take(pageIndex)
            : journey.QuestionHistory;

        var radioValues = historyBeforePage
            .SelectMany(pid =>
            {
                var p = config.Pages.FirstOrDefault(p => p.Id == pid);
                if (p is null) return Enumerable.Empty<string>();
                return p.Questions
                    .Where(q => q.Type == QuestionType.Radio && q.ContentKey)
                    .Select(q => answers.TryGetValue(q.Id, out var a) ? a.TextValue : null)
                    .Where(v => v is not null)
                    .Select(v => v!);
            });

        return string.Join("-", new[] { "journey", windowId.ToString(), whatToChange }.Concat(radioValues));
    }

    public string ResolveRequestType(QuestionFlowConfig config, RequestState journey)
    {
        // Primary: first answered question flagged UseAsRequestType in the visited branch
        foreach (var pageId in journey.QuestionHistory)
        {
            var page = GetPage(config, pageId);
            if (page is null) continue;

            foreach (var question in page.Questions)
            {
                if (!question.UseAsRequestType) continue;
                if (!journey.QuestionAnswers.TryGetValue(question.Id, out var answer)) continue;
                return ResolveAnswerLabel(question, answer);
            }
        }

        // Fallback: answer to the first question in history
        foreach (var pageId in journey.QuestionHistory)
        {
            var page = GetPage(config, pageId);
            if (page is null) continue;

            foreach (var question in page.Questions)
            {
                if (journey.QuestionAnswers.TryGetValue(question.Id, out var answer))
                    return ResolveAnswerLabel(question, answer);
            }
        }

        return string.Empty;
    }

    public string ResolveRequestTypeValue(QuestionFlowConfig config, RequestState journey)
    {
        // Raw answer value of the first answered UseAsRequestType question in the
        // visited branch. Deliberately no fallback to unflagged questions: this feeds
        // the rules engine's WhatToChange contract, so flows without a flagged
        // question must always produce the bare WhatToChange prefix.
        foreach (var pageId in journey.QuestionHistory)
        {
            var page = GetPage(config, pageId);
            if (page is null) continue;

            foreach (var question in page.Questions)
            {
                if (!question.UseAsRequestType) continue;
                if (!journey.QuestionAnswers.TryGetValue(question.Id, out var answer)) continue;
                return answer.TextValue ?? string.Empty;
            }
        }

        return string.Empty;
    }

    private static string ResolveAnswerLabel(Question question, QuestionAnswer answer)
    {
        if (question.Type == QuestionType.Radio && answer.TextValue is not null)
            return question.Options?.FirstOrDefault(o => o.Value == answer.TextValue)?.Label
                   ?? answer.TextValue;

        return answer.TextValue ?? string.Empty;
    }
}
