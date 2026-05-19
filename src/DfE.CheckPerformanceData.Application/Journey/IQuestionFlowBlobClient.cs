using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.Journey;

public interface IQuestionFlowBlobClient
{
    Task<QuestionFlowConfig?> GetConfigAsync(WhatToChange whatToChange, CheckingWindowType checkingWindowType);
    Task UploadConfigAsync(WhatToChange whatToChange, CheckingWindowType checkingWindowType, string json);
}
