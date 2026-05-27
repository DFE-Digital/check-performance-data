using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.Journey;

public interface IQuestionFlowService
{
    Task<QuestionFlowConfig?> GetConfigAsync(WhatToChange whatToChange, CheckingWindowType checkingWindowType);
    JourneyPage? GetPage(QuestionFlowConfig config, string pageId);
    string? GetNextPageId(QuestionFlowConfig config, string pageId, Dictionary<string, QuestionAnswer> answers);
    JourneyNavigation? GetNavigationGuard(QuestionFlowConfig config, RequestState journey, string pageId);
    string BuildContentKey(Guid windowId, JourneyPage page, Dictionary<string, QuestionAnswer> answers, RequestState journey, QuestionFlowConfig config);
    string ResolveRequestType(QuestionFlowConfig config, RequestState journey);
}
