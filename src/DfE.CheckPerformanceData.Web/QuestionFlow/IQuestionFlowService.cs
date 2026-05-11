using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Web.QuestionFlow;

public interface IQuestionFlowService
{
    QuestionFlowConfig? GetConfig(WhatToChange whatToChange, CheckingWindowType checkingWindowType);
    JourneyPage GetPage(QuestionFlowConfig config, string pageId);
    string? GetNextPageId(QuestionFlowConfig config, string pageId, Dictionary<string, QuestionAnswer> answers);
    List<string> BuildCurrentPath(QuestionFlowConfig config, Dictionary<string, QuestionAnswer> answers);
}
