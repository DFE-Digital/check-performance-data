using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Web.QuestionFlow;

public interface IQuestionFlowService
{
    QuestionFlowConfig? GetConfig(WhatToChange whatToChange, KeyStages keyStage);
    Question GetQuestion(QuestionFlowConfig config, string questionId);
    string? GetNextQuestionId(QuestionFlowConfig config, string questionId, QuestionAnswer? answer);
    List<string> BuildCurrentPath(QuestionFlowConfig config, Dictionary<string, QuestionAnswer> answers);
}
