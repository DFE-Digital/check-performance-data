using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Domain.Enums;
using Microsoft.Extensions.Caching.Memory;

namespace DfE.CheckPerformanceData.Application.Journey;

public sealed class QuestionFlowService(IQuestionFlowConfigSource configSource, IMemoryCache cache) : IQuestionFlowService
{
    public async Task<QuestionFlowConfig?> GetConfigAsync(WhatToChange whatToChange, CheckingWindowType checkingWindowType)
    {
        var key = $"qflow_{whatToChange}_{checkingWindowType}";
        if (cache.TryGetValue<QuestionFlowConfig>(key, out var cached))
            return cached;

        var config = await configSource.GetConfigAsync(whatToChange, checkingWindowType);
        // Only cache a config that was actually found. Caching a null with NeverRemove priority
        // pins that miss for the life of the process. Since the configs now ship in the image a
        // miss means the file genuinely is not there, but the guard stays: a cached null would
        // turn a deploy-time packaging mistake into a permanent one that survives investigation.
        if (config is not null)
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

    public JourneyPage? GetReachableEvidencePage(QuestionFlowConfig config, Dictionary<string, QuestionAnswer> answers)
    {
        var pageId = config.FirstPageId;
        var visited = new HashSet<string>();
        while (pageId is not null && visited.Add(pageId))
        {
            var page = GetPage(config, pageId);
            if (page is null) return null;
            if (page.Type == PageType.EvidenceUpload) return page;
            pageId = GetNextPageId(config, pageId, answers);
        }
        return null;
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

    public string BuildContentKey(JourneyPage page, Dictionary<string, QuestionAnswer> answers,
        RequestState journey, QuestionFlowConfig config)
    {
        var radioValues = ContentKeyQuestionsBefore(page, journey, config)
            .Select(pq => pq.Question)
            .Where(q => q.Type == QuestionType.Radio)
            .Select(q => answers.TryGetValue(q.Id, out var a) ? a.TextValue : null)
            .Where(v => v is not null)
            .Select(v => v!);

        return string.Join("-", ContentKeyPrefix(journey).Concat(radioValues));
    }

    public IReadOnlyList<EvidenceContentBlock> BuildEvidenceContentBlocks(JourneyPage page,
        Dictionary<string, QuestionAnswer> answers, RequestState journey, QuestionFlowConfig config)
    {
        // A radio adds its one value to every key. A checkbox gives one block for each ticked
        // value, in config order, so each ticked option shows its own block and one tick gives
        // the key the radio gave before (#591). After them it keeps the key it was given, as a
        // shared block for the whole section that shows whatever the school ticks (#633).
        IEnumerable<EvidenceContentBlock> blocks = [new(string.Join("-", ContentKeyPrefix(journey)), null)];
        foreach (var (questionPage, question) in ContentKeyQuestionsBefore(page, journey, config))
        {
            if (!answers.TryGetValue(question.Id, out var answer)) continue;

            if (question.Type == QuestionType.Radio && answer.TextValue is { } value)
            {
                blocks = blocks.Select(b => b with { Key = $"{b.Key}-{value}" }).ToList();
            }
            else if (question.Type == QuestionType.Checkbox)
            {
                var ticked = CheckboxAnswerDisplay.OrderedValues(question, answer);
                var sectionLabel = questionPage.Title ?? question.Title;
                if (ticked.Count > 0)
                    blocks = blocks.SelectMany(b => ticked.Select(v => new EvidenceContentBlock(
                            $"{b.Key}-{v}",
                            question.Options?.FirstOrDefault(o => o.Value == v)?.Label ?? v))
                        .Append(b with { SectionLabel = sectionLabel })).ToList();
            }
        }

        return blocks.ToList();
    }

    private static IEnumerable<string> ContentKeyPrefix(RequestState journey)
    {
        var windowType = journey.CheckingWindow?.CheckingWindowType.ToString().ToLowerInvariant() ?? "unknown";
        var whatToChange = journey.SelectedWhatToChange?.ToString().ToLower() ?? "unknown";
        return ["journey", windowType, whatToChange];
    }

    // The contentKey questions on the pages visited before this one, in visit order, with their page.
    private static IEnumerable<(JourneyPage Page, Question Question)> ContentKeyQuestionsBefore(JourneyPage page, RequestState journey,
        QuestionFlowConfig config)
    {
        var pageIndex = journey.QuestionHistory.IndexOf(page.Id);
        IEnumerable<string> historyBeforePage = pageIndex >= 0
            ? journey.QuestionHistory.Take(pageIndex)
            : journey.QuestionHistory;

        return historyBeforePage
            .Select(pid => config.Pages.FirstOrDefault(p => p.Id == pid))
            .Where(p => p is not null)
            .SelectMany(p => p!.Questions.Where(q => q.ContentKey).Select(q => (p!, q)));
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

        // AB#297310: a page whose answers ARE the pupil (e.g. the Add journey's learner-details)
        // has no "reason" or "category" to surface — its first question is a typed name, and
        // guessing a request-type description from it (or from an unrelated later page) would be
        // arbitrary and misleading (observed: "Add - Alice"). A flow built around a synthetic
        // pupil never has a meaningful sub-category, so stop here rather than falling through.
        if (journey.QuestionHistory.Any(pageId => GetPage(config, pageId)?.PupilFromAnswers == true))
            return string.Empty;

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
